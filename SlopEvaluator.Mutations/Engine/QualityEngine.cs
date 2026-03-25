using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Engine;

/// <summary>
/// Orchestrates the complete quality analysis pipeline:
/// coverage → edge cases → mutations → combined report.
/// </summary>
public sealed class QualityEngine
{
    private readonly HarnessConfig _config;
    private readonly Action<string> _log;
    private readonly MetricsCollector? _metrics;
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes the quality engine with a harness configuration.
    /// </summary>
    /// <param name="config">Configuration containing source file, test command, and mutation specs.</param>
    /// <param name="log">Optional logging callback; defaults to console output.</param>
    /// <param name="metrics">Optional metrics collector for telemetry.</param>
    /// <param name="logger">Optional structured logger for observability.</param>
    public QualityEngine(HarnessConfig config, Action<string>? log = null, MetricsCollector? metrics = null,
        ILogger? logger = null)
    {
        _config = config;
        _log = log ?? Console.WriteLine;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full quality pipeline. Each phase is optional and independent.
    /// </summary>
    public async Task<QualityReport> RunAsync(
        bool runCoverage = true,
        bool runEdgeCases = true,
        bool runMutations = true,
        string? existingCoverageReport = null,
        CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();
        _metrics?.RecordSessionStart("quality", _config.SourceFile, _config.Target, _config.Mutations.Count);

        CoverageReport? coverage = null;
        EdgeCaseReport? edgeCases = null;
        MutationReport? mutationReport = null;

        // ── Coverage ───────────────────────────────────────────────
        if (runCoverage)
        {
            _logger?.LogInformation("Quality phase starting: {Phase}", "coverage");
            _log("");
            _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
            _log("  STAGE 1 \u2014 CODE COVERAGE");
            _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

            var covSw = Stopwatch.StartNew();
            var analyzer = new CoverageAnalyzer(
                _config.TestCommand, _config.SourceFile,
                _config.TestTimeoutSeconds, _log, _metrics);

            if (existingCoverageReport is not null)
            {
                _log($"  Using existing report: {existingCoverageReport}");
                coverage = analyzer.ParseExistingReport(existingCoverageReport);
            }
            else
            {
                coverage = await analyzer.RunAsync(ct);
            }

            if (coverage is not null)
            {
                _log($"  Line coverage:   {coverage.LineCoverage:F1}%");
                _log($"  Branch coverage: {coverage.BranchCoverage:F1}%");
                _log($"  Uncovered lines: {coverage.UncoveredLines.Count}");
                _log($"  Uncovered branches: {coverage.UncoveredBranches.Count}");

                // Show methods with low coverage
                var weakMethods = coverage.Methods
                    .Where(m => m.LineCoverage < 80 || m.BranchCoverage < 80)
                    .OrderBy(m => m.BranchCoverage)
                    .Take(5)
                    .ToList();

                if (weakMethods.Count > 0)
                {
                    _log("");
                    _log("  Methods needing attention:");
                    foreach (var m in weakMethods)
                    {
                        _log($"    {m.MethodName}: {m.LineCoverage:F0}% line, {m.BranchCoverage:F0}% branch");
                    }
                }
            }
            else
            {
                _log("  \u26a0\ufe0f Coverage collection failed \u2014 continuing without coverage data.");
            }
            covSw.Stop();
            _logger?.LogInformation("Quality phase complete: {Phase} in {Duration}s", "coverage", covSw.Elapsed.TotalSeconds);
            _metrics?.RecordPhase("coverage", covSw.Elapsed.TotalMilliseconds, coverage is not null);
        }

        // ── Edge Cases ─────────────────────────────────────────────
        if (runEdgeCases)
        {
            _logger?.LogInformation("Quality phase starting: {Phase}", "edge-cases");
            _log("");
            _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
            _log("  STAGE 2 \u2014 EDGE CASE ANALYSIS");
            _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

            var edgeSw = Stopwatch.StartNew();
            var edgeAnalyzer = new EdgeCaseAnalyzer(
                _config.SourceFile, _config.Target,
                _config.RecommendedTestFile, _log);

            edgeCases = edgeAnalyzer.Analyze();
            edgeSw.Stop();
            _logger?.LogInformation("Quality phase complete: {Phase} in {Duration}s", "edge-cases", edgeSw.Elapsed.TotalSeconds);
            _metrics?.RecordPhase("edge-cases", edgeSw.Elapsed.TotalMilliseconds, true);

            if (edgeCases.EdgeCases.Count > 0)
            {
                _log("");
                var uncovered = edgeCases.EdgeCases.Where(e => e.CoveredByExistingTests != true).ToList();
                _log($"  Potentially uncovered edge cases: {uncovered.Count}");

                foreach (var group in uncovered
                    .GroupBy(e => e.Category)
                    .OrderByDescending(g => g.Count()))
                {
                    _log($"    {group.Key}: {group.Count()}");
                }
            }
        }

        // ── Mutations ──────────────────────────────────────────────
        if (runMutations && _config.Mutations.Count > 0)
        {
            _logger?.LogInformation("Quality phase starting: {Phase} with {MutationCount} mutations", "mutations", _config.Mutations.Count);
            _log("");
            _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
            _log("  STAGE 3 \u2014 MUTATION TESTING");
            _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

            var engine = new MutationEngine(_config, _log, _metrics, logger: _logger);
            mutationReport = await engine.RunAsync(ct);
            _logger?.LogInformation("Quality phase complete: {Phase}", "mutations");
        }

        // ── Build Combined Report ──────────────────────────────────
        totalSw.Stop();

        var actions = BuildActionItems(coverage, edgeCases, mutationReport);
        var scores = BuildScores(coverage, edgeCases, mutationReport);

        var report = new QualityReport
        {
            SourceFile = _config.SourceFile,
            Target = _config.Target,
            RunDate = DateTime.UtcNow,
            TotalDuration = totalSw.Elapsed,
            Coverage = coverage,
            EdgeCases = edgeCases,
            MutationResults = mutationReport,
            Scores = scores,
            Actions = actions
        };

        PrintSummary(report);

        _metrics?.RecordPhase("total", totalSw.Elapsed.TotalMilliseconds, true);
        _metrics?.RecordAggregateStats(mutationReport, coverage, edgeCases, totalSw.Elapsed.TotalSeconds);

        return report;
    }

    private static QualityScores BuildScores(
        CoverageReport? coverage,
        EdgeCaseReport? edgeCases,
        MutationReport? mutation)
    {
        double edgeCaseCoverage = 0;
        if (edgeCases is not null && edgeCases.EdgeCases.Count > 0)
        {
            var covered = edgeCases.EdgeCases.Count(e => e.CoveredByExistingTests == true);
            edgeCaseCoverage = (double)covered / edgeCases.EdgeCases.Count * 100;
        }

        return new QualityScores
        {
            LineCoverage = coverage?.LineCoverage ?? 0,
            BranchCoverage = coverage?.BranchCoverage ?? 0,
            MutationScore = mutation?.MutationScore ?? 0,
            EdgeCaseCoverage = edgeCaseCoverage
        };
    }

    private static List<QualityAction> BuildActionItems(
        CoverageReport? coverage,
        EdgeCaseReport? edgeCases,
        MutationReport? mutation)
    {
        var actions = new List<QualityAction>();

        // High priority: uncovered branches
        if (coverage is not null)
        {
            foreach (var branch in coverage.UncoveredBranches.Take(10))
            {
                actions.Add(new QualityAction
                {
                    Priority = "high",
                    Category = "uncovered-branch",
                    Description = $"Branch not covered: {branch.SourceSnippet ?? $"line {branch.LineNumber}"} " +
                                  $"(condition: {branch.Condition})",
                    File = coverage.SourceFile,
                    LineNumber = branch.LineNumber
                });
            }

            // Methods with zero branch coverage
            foreach (var method in coverage.Methods.Where(m => m.BranchesValid > 0 && m.BranchCoverage == 0))
            {
                actions.Add(new QualityAction
                {
                    Priority = "high",
                    Category = "untested-method",
                    Description = $"Method '{method.MethodName}' has branches but 0% branch coverage",
                    File = coverage.SourceFile,
                    LineNumber = null
                });
            }
        }

        // High priority: surviving mutations
        if (mutation is not null)
        {
            foreach (var survivor in mutation.Results.Where(r => r.Outcome == MutationOutcome.Survived))
            {
                actions.Add(new QualityAction
                {
                    Priority = survivor.RiskLevel == "high" ? "critical" : "high",
                    Category = "surviving-mutant",
                    Description = $"Mutation survived: {survivor.Description} " +
                                  $"(strategy: {survivor.Strategy})",
                    File = mutation.SourceFile,
                    LineNumber = null
                });
            }
        }

        // Medium priority: uncovered edge cases
        if (edgeCases is not null)
        {
            foreach (var ec in edgeCases.EdgeCases
                .Where(e => e.CoveredByExistingTests != true)
                .OrderByDescending(e => e.RiskLevel == "high" ? 3 : e.RiskLevel == "medium" ? 2 : 1)
                .Take(15))
            {
                actions.Add(new QualityAction
                {
                    Priority = ec.RiskLevel == "high" ? "high" : "medium",
                    Category = "edge-case-gap",
                    Description = $"[{ec.Category}] {ec.Description} \u2192 {ec.SuggestedTestName}",
                    File = edgeCases.SourceFile,
                    LineNumber = ec.LineNumber
                });
            }
        }

        // Low priority: uncovered lines (bulk)
        if (coverage is not null && coverage.UncoveredLines.Count > 0)
        {
            actions.Add(new QualityAction
            {
                Priority = "low",
                Category = "uncovered-lines",
                Description = $"{coverage.UncoveredLines.Count} lines not covered by any test",
                File = coverage.SourceFile,
                LineNumber = coverage.UncoveredLines.FirstOrDefault()?.LineNumber
            });
        }

        return actions
            .OrderBy(a => a.Priority switch
            {
                "critical" => 0,
                "high" => 1,
                "medium" => 2,
                "low" => 3,
                _ => 4
            })
            .ToList();
    }

    private void PrintSummary(QualityReport report)
    {
        _log("");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        _log($"  QUALITY REPORT \u2014 {report.Target ?? report.SourceFile}");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        _log("");
        _log("  SCORES");
        _log($"  \u251c\u2500\u2500 Line Coverage:      {report.Scores.LineCoverage:F1}%");
        _log($"  \u251c\u2500\u2500 Branch Coverage:    {report.Scores.BranchCoverage:F1}%");
        _log($"  \u251c\u2500\u2500 Mutation Score:     {report.Scores.MutationScore:F1}%");
        _log($"  \u251c\u2500\u2500 Edge Case Coverage: {report.Scores.EdgeCaseCoverage:F1}%");
        _log($"  \u2514\u2500\u2500 Composite Score:    {report.Scores.CompositeScore:F1}%");
        _log("");

        // Action summary by priority
        var byPriority = report.Actions.GroupBy(a => a.Priority).OrderBy(g => g.Key);
        _log("  ACTION ITEMS");
        foreach (var group in byPriority)
        {
            var icon = group.Key switch
            {
                "critical" => "\ud83d\udd34",
                "high" => "\ud83d\udfe0",
                "medium" => "\ud83d\udfe1",
                "low" => "\ud83d\udfe2",
                _ => "\u26aa"
            };
            _log($"  {icon} {group.Key.ToUpper()}: {group.Count()} items");
        }

        // Top 5 actions
        _log("");
        _log("  TOP PRIORITIES");
        foreach (var action in report.Actions.Take(5))
        {
            var icon = action.Priority switch
            {
                "critical" => "\ud83d\udd34",
                "high" => "\ud83d\udfe0",
                "medium" => "\ud83d\udfe1",
                _ => "\ud83d\udfe2"
            };
            var line = action.LineNumber.HasValue ? $" (line {action.LineNumber})" : "";
            _log($"  {icon} [{action.Category}] {action.Description}{line}");
        }

        if (report.Actions.Count > 5)
            _log($"  ... and {report.Actions.Count - 5} more");

        _log("");
        _log($"  Total time: {report.TotalDuration.TotalSeconds:F1}s");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
    }
}
