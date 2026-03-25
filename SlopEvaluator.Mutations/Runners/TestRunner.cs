using System.Diagnostics;
using System.Text.RegularExpressions;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Runners;

public sealed record TestRunResult(
    bool Success,
    int ExitCode,
    int TestsRun,
    int TestsPassed,
    int TestsFailed,
    TimeSpan Duration,
    string StandardOutput,
    string StandardError,
    string? FailedTestNames,
    long? PeakWorkingSetBytes = null
);

public sealed record BuildRunResult(
    bool Success,
    string Output,
    TimeSpan Duration,
    long? PeakWorkingSetBytes = null
);

public sealed partial class TestRunner
{
    private readonly string _testCommand;
    private readonly int _timeoutSeconds;

    public TestRunner(string testCommand, int timeoutSeconds = 120)
    {
        _testCommand = testCommand;
        _timeoutSeconds = timeoutSeconds;
    }

    /// <summary>
    /// Runs the test command and parses the results.
    /// Returns null if the process couldn't start.
    /// </summary>
    public async Task<TestRunResult> RunAsync(CancellationToken ct = default)
    {
        var parts = ParseCommand(_testCommand);
        var sw = Stopwatch.StartNew();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = parts.Executable,
                Arguments = parts.Arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — not user cancellation
                TryKill(process);
                sw.Stop();
                return new TestRunResult(
                    Success: false,
                    ExitCode: -1,
                    TestsRun: 0,
                    TestsPassed: 0,
                    TestsFailed: 0,
                    Duration: sw.Elapsed,
                    StandardOutput: stdout.ToString(),
                    StandardError: "TIMEOUT: Test run exceeded " + _timeoutSeconds + "s",
                    FailedTestNames: null
                );
            }

            sw.Stop();
            var peakMem = TryGetPeakWorkingSet(process);
            var stdoutStr = stdout.ToString();
            var stderrStr = stderr.ToString();
            var (run, passed, failed) = ParseTestCounts(stdoutStr);
            var failedNames = ExtractFailedTestNames(stdoutStr);

            return new TestRunResult(
                Success: process.ExitCode == 0,
                ExitCode: process.ExitCode,
                TestsRun: run,
                TestsPassed: passed,
                TestsFailed: failed,
                Duration: sw.Elapsed,
                StandardOutput: stdoutStr,
                StandardError: stderrStr,
                FailedTestNames: failedNames,
                PeakWorkingSetBytes: peakMem
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestRunResult(
                Success: false,
                ExitCode: -999,
                TestsRun: 0,
                TestsPassed: 0,
                TestsFailed: 0,
                Duration: sw.Elapsed,
                StandardOutput: "",
                StandardError: $"Process error: {ex.Message}",
                FailedTestNames: null
            );
        }
    }

    /// <summary>
    /// Runs dotnet build and returns a BuildRunResult with timing and memory.
    /// </summary>
    public async Task<BuildRunResult> BuildAsync(
        string projectPath, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" -v q",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            sw.Stop();
            return new BuildRunResult(false, "Build timed out after 60s", sw.Elapsed);
        }

        sw.Stop();
        return new BuildRunResult(
            process.ExitCode == 0,
            output.ToString(),
            sw.Elapsed,
            TryGetPeakWorkingSet(process));
    }

    private static (string Executable, string Arguments) ParseCommand(string command)
    {
        var trimmed = command.Trim();

        // Handle "dotnet test ..." as the most common case
        if (trimmed.StartsWith("dotnet ", StringComparison.OrdinalIgnoreCase))
        {
            var args = trimmed["dotnet ".Length..].Trim();
            return ("dotnet", args);
        }

        // Generic: split on first space
        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
            return (trimmed, "");

        return (trimmed[..spaceIndex], trimmed[(spaceIndex + 1)..]);
    }

    private static (int Run, int Passed, int Failed) ParseTestCounts(string output)
    {
        // Try multiple formats: xUnit, NUnit, MSTest all differ
        // xUnit: "Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12"
        // NUnit: "Test Count: 12, Passed: 12, Failed: 0"
        // MSTest: "Total tests: 12. Passed: 12. Failed: 0."
        // .NET 8+ summary: "Passed: 12, Failed: 0, Total: 12"

        var totalMatch = TotalTestsRegex().Match(output);
        var passedMatch = PassedRegex().Match(output);
        var failedMatch = FailedRegex().Match(output);

        int total = totalMatch.Success ? int.Parse(totalMatch.Groups[1].Value) : 0;
        int passed = passedMatch.Success ? int.Parse(passedMatch.Groups[1].Value) : 0;
        int failed = failedMatch.Success ? int.Parse(failedMatch.Groups[1].Value) : 0;

        // Also try NUnit "Test Count:" format
        if (total == 0)
        {
            var testCountMatch = TestCountRegex().Match(output);
            if (testCountMatch.Success)
                total = int.Parse(testCountMatch.Groups[1].Value);
        }

        // Derive total from passed + failed if still missing
        if (total == 0 && (passed > 0 || failed > 0))
            total = passed + failed;

        // Cross-validate: if passed + failed > total, trust the sum
        if (passed + failed > total)
            total = passed + failed;

        return (total, passed, failed);
    }

    private static string? ExtractFailedTestNames(string output)
    {
        var matches = FailedTestNameRegex().Matches(output);
        if (matches.Count == 0) return null;

        var names = matches.Select(m => m.Groups[1].Value.Trim()).Distinct();
        return string.Join(", ", names);
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
    }

    private static long? TryGetPeakWorkingSet(Process process)
    {
        try { return process.PeakWorkingSet64; } catch { return null; }
    }

    // Anchored to common test runner summary line patterns to avoid
    // matching test names like "TestPassed" or log lines with numbers
    [GeneratedRegex(@"(?:^|\s|,)\s*Total:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TotalTestsRegex();

    [GeneratedRegex(@"Test Count:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TestCountRegex();

    [GeneratedRegex(@"(?:^|\s|,)\s*Passed:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PassedRegex();

    [GeneratedRegex(@"(?:^|\s|,)\s*Failed:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FailedRegex();

    [GeneratedRegex(@"^\s*(?:Failed|FAILED|\[FAILED\])\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FailedTestNameRegex();
}
