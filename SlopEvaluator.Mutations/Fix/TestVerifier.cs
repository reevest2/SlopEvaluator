using System.Diagnostics;
using SlopEvaluator.Mutations.Fix;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Fix;

public static class TestVerifier
{
    /// <summary>
    /// Runs the generated tests against the ORIGINAL code to verify they pass.
    /// A killing test must pass on original code (and would fail on the mutant).
    /// </summary>
    public static async Task<bool> VerifyAsync(
        string testProjectPath, string testClassName,
        int timeoutSeconds = 120, Action<string>? log = null)
    {
        log ??= Console.WriteLine;

        var filter = $"FullyQualifiedName~{testClassName}";
        var args = $"test \"{testProjectPath}\" --filter {filter} --verbosity minimal";

        log($"  Verifying: dotnet {args}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            log("  ❌ Verification timed out");
            return false;
        }

        if (process.ExitCode == 0)
        {
            log("  ✅ All generated tests pass on original code");
            return true;
        }
        else
        {
            log("  ❌ Some generated tests FAIL on original code — tests may be incorrect");
            log($"  Output: {output.ToString().Trim()[..Math.Min(500, output.Length)]}");
            return false;
        }
    }
}
