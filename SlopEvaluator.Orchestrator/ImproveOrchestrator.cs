using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;
using SlopEvaluator.Mutations.Models;

namespace SlopEvaluator.Orchestrator;

/// <summary>
/// The core `slop improve` workflow: scan -> identify weak spots -> mutate -> feedback.
/// </summary>
public sealed class ImproveOrchestrator
{
    private readonly ILoggerFactory _loggerFactory;

    public ImproveOrchestrator(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Run the full improve loop: scan, identify weaknesses, mutate targets, analyze results.
    /// </summary>
    /// <param name="path">Path to the project root.</param>
    /// <param name="name">Optional codebase name.</param>
    /// <param name="maxTargets">Maximum number of files to mutate.</param>
    /// <returns>The improve result with before/after scores and mutation reports.</returns>
    public async Task<ImproveResult> ImproveAsync(string path, string? name = null, int maxTargets = 3)
    {
        path = Path.GetFullPath(path);
        name ??= Path.GetFileName(path);

        // Step 1: Scan
        Console.WriteLine("  Step 1: Running health scan...\n");
        var scanner = new ScanOrchestrator(_loggerFactory);
        var (codebase, delta) = await scanner.ScanAsync(path, name);

        PrintHealthSummary(codebase);

        // Step 2: Identify weak targets
        Console.WriteLine("\n  Step 2: Identifying weak spots...\n");
        var selector = new TargetSelector();
        var targets = selector.SelectTargets(codebase, path, maxTargets);

        if (targets.Count == 0)
        {
            Console.WriteLine("  No suitable mutation targets found.");
            return new ImproveResult
            {
                BeforeScore = codebase.Score,
                AfterScore = codebase.Score,
                Codebase = codebase,
                Targets = [],
                Reports = [],
                Delta = delta
            };
        }

        Console.WriteLine($"  Found {targets.Count} target(s):");
        foreach (var target in targets)
        {
            Console.WriteLine($"    - {Path.GetFileName(target.FilePath)} (weakness: {target.WeaknessScore:F2})");
            Console.WriteLine($"      {target.Reason}");
        }

        // Step 3: Auto-detect test command
        var testCommand = MutationOrchestrator.DetectTestCommand(path);
        if (testCommand is null)
        {
            Console.WriteLine("\n  Could not auto-detect test command. Skipping mutation phase.");
            return new ImproveResult
            {
                BeforeScore = codebase.Score,
                AfterScore = codebase.Score,
                Codebase = codebase,
                Targets = targets,
                Reports = [],
                Delta = delta
            };
        }

        // Step 4: Mutate each target
        Console.WriteLine($"\n  Step 3: Running mutations (test cmd: {testCommand})...\n");
        var mutator = new MutationOrchestrator(_loggerFactory);
        var reports = new List<MutationReport>();

        foreach (var target in targets)
        {
            Console.WriteLine($"\n  Mutating: {Path.GetFileName(target.FilePath)}");
            try
            {
                var report = await mutator.MutateAsync(
                    target.FilePath, testCommand, projectPath: path, maxMutations: 10);
                reports.Add(report);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Skipped: {ex.Message}");
            }
        }

        // Step 5: Feedback loop — update health score with mutation results
        Console.WriteLine("\n  Step 4: Computing updated health score...\n");
        var feedback = new FeedbackLoop();
        var updated = feedback.UpdateHealthScore(codebase, reports);

        PrintScoreComparison(codebase.Score, updated.Score);

        return new ImproveResult
        {
            BeforeScore = codebase.Score,
            AfterScore = updated.Score,
            Codebase = updated,
            Targets = targets,
            Reports = reports,
            Delta = delta
        };
    }

    private static void PrintHealthSummary(Codebase codebase)
    {
        Console.WriteLine($"  Overall Health: {codebase.Score:F3}");
        Console.WriteLine($"  Testing:        {codebase.Testing.Score:F3}");
        Console.WriteLine($"  Code Quality:   {codebase.Quality.Score:F3}");
        Console.WriteLine($"  Security:       {codebase.Security.Score:F3}");
    }

    private static void PrintScoreComparison(double before, double after)
    {
        var delta = after - before;
        var arrow = delta > 0 ? "+" : delta < 0 ? "" : "=";
        Console.WriteLine($"  Before: {before:F3}  After: {after:F3}  ({arrow}{delta:F3})");
    }
}

/// <summary>
/// Result of the improve workflow.
/// </summary>
public sealed class ImproveResult
{
    public required double BeforeScore { get; init; }
    public required double AfterScore { get; init; }
    public required Codebase Codebase { get; init; }
    public required List<MutationTarget> Targets { get; init; }
    public required List<MutationReport> Reports { get; init; }
    public SnapshotDelta? Delta { get; init; }

    public double ScoreDelta => AfterScore - BeforeScore;
    public int TotalMutantsKilled => Reports.Sum(r => r.Killed);
    public int TotalMutantsSurvived => Reports.Sum(r => r.Survived);
}
