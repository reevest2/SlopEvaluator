using System.Diagnostics;
using System.Xml.Linq;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;

namespace SlopEvaluator.Mutations.Analysis;

/// <summary>
/// Runs dotnet test with Coverlet collection and parses the Cobertura XML output.
/// </summary>
public sealed class CoverageAnalyzer
{
    private readonly string _testCommand;
    private readonly string _sourceFile;
    private readonly int _timeoutSeconds;
    private readonly Action<string> _log;
    private readonly MetricsCollector? _metrics;

    /// <summary>
    /// Initializes a new coverage analyzer for the given source file.
    /// </summary>
    /// <param name="testCommand">The shell command used to run tests (e.g. "dotnet test").</param>
    /// <param name="sourceFile">Absolute or relative path to the source file under analysis.</param>
    /// <param name="timeoutSeconds">Maximum seconds to wait for the test process.</param>
    /// <param name="log">Optional logging callback; defaults to console output.</param>
    /// <param name="metrics">Optional metrics collector for telemetry.</param>
    public CoverageAnalyzer(
        string testCommand,
        string sourceFile,
        int timeoutSeconds = 120,
        Action<string>? log = null,
        MetricsCollector? metrics = null)
    {
        _testCommand = testCommand;
        _sourceFile = Path.GetFullPath(sourceFile);
        _timeoutSeconds = timeoutSeconds;
        _log = log ?? Console.WriteLine;
        _metrics = metrics;
    }

    /// <summary>
    /// Runs test coverage collection and returns a parsed report.
    /// </summary>
    public async Task<CoverageReport?> RunAsync(CancellationToken ct = default)
    {
        var coverageDir = Path.Combine(Path.GetTempPath(), $"mutation-harness-cov-{Guid.NewGuid():N}");
        Directory.CreateDirectory(coverageDir);
        var coberturaPath = Path.Combine(coverageDir, "coverage.cobertura.xml");

        try
        {
            _log("  Running tests with coverage collection...");

            // Append Coverlet args to the test command
            var coverageCmd = BuildCoverageCommand(coberturaPath);
            var result = await RunCommandAsync(coverageCmd, ct);

            if (!result.Success)
            {
                _log("  \u26a0\ufe0f Test run failed during coverage collection.");
                _log($"  {result.Output}");

                // Try to parse partial coverage anyway
                if (!File.Exists(coberturaPath))
                    return null;
            }

            if (!File.Exists(coberturaPath))
            {
                _log("  \u26a0\ufe0f Coverage file not generated. Ensure Coverlet is installed:");
                _log("     dotnet add <test-project> package coverlet.collector");
                return null;
            }

            _log("  Parsing coverage report...");
            var report = ParseCoberturaXml(coberturaPath);
            return report;
        }
        finally
        {
            // Cleanup temp coverage files
            try { Directory.Delete(coverageDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Parses an existing Cobertura XML file without running tests.
    /// Useful if the user already has a coverage report.
    /// </summary>
    public CoverageReport ParseExistingReport(string coberturaPath)
    {
        if (!File.Exists(coberturaPath))
            throw new FileNotFoundException($"Coverage report not found: {coberturaPath}");

        return ParseCoberturaXml(coberturaPath);
    }

    private string BuildCoverageCommand(string outputPath)
    {
        // Coverlet via dotnet test uses --collect and --results-directory
        var baseCmd = _testCommand.Trim();

        // If user already has collect args, don't double up
        if (baseCmd.Contains("--collect", StringComparison.OrdinalIgnoreCase))
            return baseCmd;

        var dir = Path.GetDirectoryName(outputPath)!;

        return $"{baseCmd} --collect:\"XPlat Code Coverage\" " +
               $"--results-directory \"{dir}\" " +
               "-- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura " +
               "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.IncludeTestAssembly=false";
    }

    private CoverageReport ParseCoberturaXml(string path)
    {
        // Coverlet sometimes nests the file one directory deep
        var actualPath = path;
        if (!File.Exists(actualPath))
        {
            var dir = Path.GetDirectoryName(path)!;
            var nested = Directory.GetFiles(dir, "coverage.cobertura.xml", SearchOption.AllDirectories);
            if (nested.Length > 0)
                actualPath = nested[0];
            else
                throw new FileNotFoundException($"Coverage file not found at {path} or subdirectories");
        }

        var doc = XDocument.Load(actualPath);
        var root = doc.Root!;

        // Parse top-level attributes
        var lineCoverage = ParseDouble(root.Attribute("line-rate")?.Value) * 100;
        var branchCoverage = ParseDouble(root.Attribute("branch-rate")?.Value) * 100;
        var linesValid = ParseInt(root.Attribute("lines-valid")?.Value);
        var linesCovered = ParseInt(root.Attribute("lines-covered")?.Value);
        var branchesValid = ParseInt(root.Attribute("branches-valid")?.Value);
        var branchesCovered = ParseInt(root.Attribute("branches-covered")?.Value);

        // Read source lines for snippets
        string[]? sourceLines = null;
        if (File.Exists(_sourceFile))
            sourceLines = File.ReadAllLines(_sourceFile);

        // Parse per-file coverage to find our target file
        var uncoveredLines = new List<UncoveredLine>();
        var uncoveredBranches = new List<UncoveredBranch>();
        var methods = new List<MethodCoverage>();

        var packages = root.Elements("packages").Elements("package");
        foreach (var pkg in packages)
        {
            var classes = pkg.Elements("classes").Elements("class");
            foreach (var cls in classes)
            {
                var filename = cls.Attribute("filename")?.Value ?? "";

                // Match against our source file (normalize paths)
                if (!IsMatchingFile(filename))
                    continue;

                var className = cls.Attribute("name")?.Value ?? "Unknown";

                // Parse lines
                var lines = cls.Elements("lines").Elements("line");
                foreach (var line in lines)
                {
                    var lineNum = ParseInt(line.Attribute("number")?.Value);
                    var hits = ParseInt(line.Attribute("hits")?.Value);
                    var isBranch = line.Attribute("branch")?.Value == "True";
                    var condCoverage = line.Attribute("condition-coverage")?.Value;

                    if (hits == 0 && lineNum > 0)
                    {
                        uncoveredLines.Add(new UncoveredLine
                        {
                            LineNumber = lineNum,
                            SourceSnippet = GetSnippet(sourceLines, lineNum)
                        });
                    }

                    // Parse branch coverage on this line
                    if (isBranch && condCoverage is not null)
                    {
                        // Format: "50% (1/2)" meaning 1 of 2 branches covered
                        var branches = ParseBranchCondition(condCoverage);
                        if (branches.Total > branches.Covered)
                        {
                            for (int b = branches.Covered; b < branches.Total; b++)
                            {
                                uncoveredBranches.Add(new UncoveredBranch
                                {
                                    LineNumber = lineNum,
                                    BranchIndex = b,
                                    Condition = condCoverage,
                                    SourceSnippet = GetSnippet(sourceLines, lineNum)
                                });
                            }
                        }
                    }
                }

                // Parse methods
                var methodElements = cls.Elements("methods").Elements("method");
                foreach (var method in methodElements)
                {
                    var methodName = method.Attribute("name")?.Value ?? "Unknown";
                    var methodSig = method.Attribute("signature")?.Value ?? "";
                    var mLineCov = ParseDouble(method.Attribute("line-rate")?.Value) * 100;
                    var mBranchCov = ParseDouble(method.Attribute("branch-rate")?.Value) * 100;

                    var methodLines = method.Elements("lines").Elements("line").ToList();
                    var mLinesValid = methodLines.Count;
                    var mLinesCovered = methodLines.Count(l => ParseInt(l.Attribute("hits")?.Value) > 0);
                    var mBranchesValid = methodLines.Count(l => l.Attribute("branch")?.Value == "True");
                    var mBranchesCovered = methodLines.Count(l =>
                        l.Attribute("branch")?.Value == "True" &&
                        l.Attribute("condition-coverage")?.Value?.StartsWith("100%") == true);

                    var uncoveredMethodLines = methodLines
                        .Where(l => ParseInt(l.Attribute("hits")?.Value) == 0)
                        .Select(l => ParseInt(l.Attribute("number")?.Value))
                        .Where(n => n > 0)
                        .ToList();

                    methods.Add(new MethodCoverage
                    {
                        ClassName = className,
                        MethodName = methodName,
                        MethodSignature = methodSig,
                        LineCoverage = mLineCov,
                        BranchCoverage = mBranchCov,
                        LinesValid = mLinesValid,
                        LinesCovered = mLinesCovered,
                        BranchesValid = mBranchesValid,
                        BranchesCovered = mBranchesCovered,
                        UncoveredLineNumbers = uncoveredMethodLines
                    });
                }
            }
        }

        return new CoverageReport
        {
            SourceFile = _sourceFile,
            GeneratedAt = DateTime.UtcNow,
            LineCoverage = lineCoverage,
            BranchCoverage = branchCoverage,
            LinesValid = linesValid,
            LinesCovered = linesCovered,
            BranchesValid = branchesValid,
            BranchesCovered = branchesCovered,
            UncoveredLines = uncoveredLines,
            UncoveredBranches = uncoveredBranches,
            Methods = methods
        };
    }

    private bool IsMatchingFile(string reportPath)
    {
        // Normalize both paths for comparison
        var normalizedReport = reportPath.Replace('\\', '/').TrimEnd('/');
        var normalizedSource = _sourceFile.Replace('\\', '/').TrimEnd('/');

        // Exact match
        if (string.Equals(normalizedReport, normalizedSource, StringComparison.OrdinalIgnoreCase))
            return true;

        // Report path might be relative or just filename
        if (normalizedSource.EndsWith(normalizedReport, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check just the filename as last resort
        var reportFilename = Path.GetFileName(normalizedReport);
        var sourceFilename = Path.GetFileName(normalizedSource);
        return string.Equals(reportFilename, sourceFilename, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetSnippet(string[]? lines, int lineNumber)
    {
        if (lines is null || lineNumber <= 0 || lineNumber > lines.Length)
            return null;

        return lines[lineNumber - 1].Trim();
    }

    private static (int Covered, int Total) ParseBranchCondition(string condition)
    {
        // Parse "50% (1/2)" format
        var parenStart = condition.IndexOf('(');
        var parenEnd = condition.IndexOf(')');
        if (parenStart < 0 || parenEnd < 0) return (0, 0);

        var fraction = condition[(parenStart + 1)..parenEnd];
        var parts = fraction.Split('/');
        if (parts.Length != 2) return (0, 0);

        return (ParseInt(parts[0]), ParseInt(parts[1]));
    }

    private static double ParseDouble(string? value) =>
        double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static int ParseInt(string? value) =>
        int.TryParse(value, out var i) ? i : 0;

    private async Task<(bool Success, string Output)> RunCommandAsync(
        string command, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var parts = command.Trim().Split(' ', 2);
        var exe = parts[0];
        var args = parts.Length > 1 ? parts[1] : "";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
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
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            sw.Stop();
            _metrics?.RecordProcessExecution("coverage-collection", "", -1,
                sw.Elapsed.TotalMilliseconds, 0, 0);
            return (false, "Timeout");
        }

        sw.Stop();
        var outputStr = output.ToString();
        long? peakMem = null;
        try { peakMem = process.PeakWorkingSet64; } catch { }

        _metrics?.RecordProcessExecution("coverage-collection", "", process.ExitCode,
            sw.Elapsed.TotalMilliseconds,
            System.Text.Encoding.UTF8.GetByteCount(outputStr), 0,
            peakMem);

        return (process.ExitCode == 0, outputStr);
    }
}
