using Microsoft.Extensions.Logging;
using SlopEvaluator.Orchestrator;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Diagnostic report with actionable recommendations.</summary>
public static class ReportCommand
{
    /// <summary>
    /// Run the diagnostic report command.
    /// </summary>
    /// <param name="args">CLI arguments: slop report &lt;path&gt; [--targets N].</param>
    /// <param name="loggerFactory">Logger factory for diagnostic output.</param>
    /// <returns>Always returns 0.</returns>
    public static async Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        // Parse: slop report <path> [--targets N]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: slop report <path> [--targets N]");
            return 1;
        }

        var path = Path.GetFullPath(args[1]);
        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Directory not found: {path}");
            return 1;
        }

        int maxTargets = 5;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--targets", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var t))
            {
                maxTargets = t;
                break;
            }
        }

        Console.WriteLine($"  Running diagnostic report: {path}");
        Console.WriteLine($"  Max targets: {maxTargets}\n");

        var orchestrator = new ReportOrchestrator(loggerFactory);
        var result = await orchestrator.GenerateAsync(path, name: null, maxTargets);

        // 1. Health dashboard
        ScanCommand.PrintHealthDashboard(result.Codebase);

        // 2. Weak dimensions
        PrintWeakDimensions(result);

        // 3. Weak files — mutation analysis
        PrintFileAnalyses(result);

        // 4. Mutation coverage gaps
        PrintCoverageGaps(result);

        // 5. Recommended next steps
        PrintNextSteps(result);

        // 6. Delta
        PrintDelta(result);

        return 0;
    }

    private static void PrintWeakDimensions(ReportResult result)
    {
        if (result.WeakDimensions.Count == 0)
        {
            Console.WriteLine("\n  No weak dimensions found (all >= 0.70). Nice!\n");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("  WEAK DIMENSIONS");
        Console.WriteLine($"  {new string('-', 60)}");

        foreach (var dim in result.WeakDimensions)
        {
            Console.WriteLine($"\n  {dim.Name} ({dim.Score:F3})");
            Console.WriteLine($"    Why:  {dim.WhyLow}");
            if (dim.Actions.Count > 0)
            {
                Console.Write("    Fix:  ");
                for (int i = 0; i < dim.Actions.Count; i++)
                {
                    if (i == 0)
                        Console.WriteLine($"- {dim.Actions[i]}");
                    else
                        Console.WriteLine($"          - {dim.Actions[i]}");
                }
            }
        }
    }

    private static void PrintFileAnalyses(ReportResult result)
    {
        if (result.FileAnalyses.Count == 0)
        {
            Console.WriteLine("\n  No weak files found for mutation analysis.\n");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("  WEAK FILES -- MUTATION ANALYSIS");
        Console.WriteLine($"  {new string('-', 60)}");

        for (int i = 0; i < result.FileAnalyses.Count; i++)
        {
            var f = result.FileAnalyses[i];
            Console.WriteLine($"\n  {i + 1}. {Path.GetFileName(f.FilePath)} (weakness: {f.WeaknessScore:F2})");
            Console.WriteLine($"     Reason: {f.Reason}");

            if (f.MutationCandidateCount > 0)
            {
                var breakdown = string.Join(", ", f.StrategyBreakdown);
                Console.WriteLine($"     Mutation candidates: {f.MutationCandidateCount} ({breakdown})");
            }
            else
            {
                Console.WriteLine("     Mutation candidates: 0 (could not analyze)");
            }

            Console.WriteLine($"     Run: {f.SuggestedCommand}");
        }
    }

    private static void PrintCoverageGaps(ReportResult result)
    {
        if (result.CoverageGaps.Count == 0)
        {
            Console.WriteLine("\n  No significant mutation coverage gaps found.\n");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("  MUTATION COVERAGE GAPS");
        Console.WriteLine($"  {new string('-', 60)}");

        foreach (var gap in result.CoverageGaps)
        {
            var testStatus = gap.HasMatchingTestFile
                ? $"has tests ({gap.TestFilePath})"
                : "NO matching test file";
            Console.WriteLine($"\n  {gap.SourceFile}");
            Console.WriteLine($"    Candidates: {gap.CandidateCount} | Tests: {testStatus} | Severity: {gap.GapSeverity:F2}");
        }
    }

    private static void PrintNextSteps(ReportResult result)
    {
        Console.WriteLine();
        Console.WriteLine("  RECOMMENDED NEXT STEPS");
        Console.WriteLine($"  {new string('-', 60)}");

        int step = 1;

        // Recommend fixing weak dimensions first
        if (result.WeakDimensions.Count > 0)
        {
            var worst = result.WeakDimensions[0];
            Console.WriteLine($"  {step++}. Focus on weakest dimension: {worst.Name} ({worst.Score:F3})");
            if (worst.Actions.Count > 0)
                Console.WriteLine($"       Action: {worst.Actions[0]}");
        }

        // Recommend running mutation testing on weak files
        if (result.FileAnalyses.Count > 0)
        {
            var topFile = result.FileAnalyses[0];
            Console.WriteLine($"  {step++}. Run mutation testing: {topFile.SuggestedCommand}");
        }

        // Recommend creating tests for coverage gaps
        var untested = result.CoverageGaps.Where(g => !g.HasMatchingTestFile).ToList();
        if (untested.Count > 0)
        {
            Console.WriteLine($"  {step++}. Create test files for {untested.Count} untested source file(s):");
            foreach (var gap in untested.Take(3))
                Console.WriteLine($"       - {gap.SourceFile} ({gap.CandidateCount} mutation candidates)");
        }

        // Recommend full improve cycle
        Console.WriteLine($"  {step++}. Run full improvement cycle: slop improve \"{result.Codebase.Name}\"");

        // Recommend gate for CI
        Console.WriteLine($"  {step}. Set up CI quality gate: slop gate <path> --threshold 0.70");

        Console.WriteLine();
    }

    private static void PrintDelta(ReportResult result)
    {
        if (result.Delta is null) return;

        var delta = result.Delta;
        var arrow = delta.OverallScoreDelta > 0 ? "+" : delta.OverallScoreDelta < 0 ? "" : "=";
        Console.WriteLine($"  Delta from previous: {arrow}{delta.OverallScoreDelta:F3} ({delta.TimeBetween.TotalHours:F0}h ago)");

        if (delta.Improvements.Count > 0)
        {
            Console.WriteLine("  Improved:");
            foreach (var imp in delta.Improvements.Take(5))
                Console.WriteLine($"    {imp}");
        }
        if (delta.Regressions.Count > 0)
        {
            Console.WriteLine("  Regressed:");
            foreach (var reg in delta.Regressions.Take(5))
                Console.WriteLine($"    {reg}");
        }
    }
}
