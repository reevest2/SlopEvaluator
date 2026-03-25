using System.Text.Json;
using Microsoft.Extensions.Logging;
using SlopEvaluator.Mutations.Models;
using static SlopEvaluator.Mutations.Commands.CommandHelpers;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Engine;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Commands;

internal static class AnalysisCommands
{
    internal static async Task<int> RunCoverageAsync(CliOptions opts)
    {
        var sourceFile = opts.Source ?? opts.PositionalArg1;
        var testCommand = opts.TestCommand;
        var existingReport = opts.ReportFile;
        var reportPath = opts.Report ?? "coverage-report.json";

        if (sourceFile is null)
        {
            Console.Error.WriteLine("Usage: SlopEvaluator coverage --source <file.cs> --test-command <cmd>");
            return 1;
        }

        if (testCommand is null && existingReport is null)
        {
            Console.Error.WriteLine("Provide --test-command to collect coverage or --report-file for existing Cobertura XML.");
            return 1;
        }

        PrintHeader("CODE COVERAGE ANALYSIS");
        Console.WriteLine($"  Source: {sourceFile}");

        var timeout = int.TryParse(opts.Timeout, out var t) ? t : 120;
        var metrics = CreateMetrics(opts);
        var analyzer = new CoverageAnalyzer(
            testCommand ?? "dotnet test", sourceFile, timeout, metrics: metrics);

        CoverageReport? coverage = existingReport is not null
            ? analyzer.ParseExistingReport(existingReport)
            : await analyzer.RunAsync();

        if (coverage is null)
        {
            Console.Error.WriteLine("Coverage collection failed. Ensure coverlet.collector is installed.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"  Line coverage:      {coverage.LineCoverage:F1}% ({coverage.LinesCovered}/{coverage.LinesValid})");
        Console.WriteLine($"  Branch coverage:    {coverage.BranchCoverage:F1}% ({coverage.BranchesCovered}/{coverage.BranchesValid})");
        Console.WriteLine($"  Uncovered lines:    {coverage.UncoveredLines.Count}");
        Console.WriteLine($"  Uncovered branches: {coverage.UncoveredBranches.Count}");

        if (coverage.UncoveredBranches.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Uncovered branches:");
            foreach (var b in coverage.UncoveredBranches.Take(10))
                Console.WriteLine($"    Line {b.LineNumber}: {b.SourceSnippet ?? "?"} [{b.Condition}]");
            if (coverage.UncoveredBranches.Count > 10)
                Console.WriteLine($"    ... and {coverage.UncoveredBranches.Count - 10} more");
        }

        var json = JsonSerializer.Serialize(coverage, JsonOptions);
        await File.WriteAllTextAsync(reportPath, json);

        var metricsPath = Path.ChangeExtension(reportPath, ".metrics.jsonl");
        await FlushMetrics(metrics, metricsPath);

        Console.WriteLine($"\n  Report: {reportPath}");
        if (metrics is not null)
            Console.WriteLine($"  Metrics: {metricsPath}");

        return coverage.BranchCoverage < 80 ? 1 : 0;
    }

    internal static async Task<int> RunEdgeCasesAsync(CliOptions opts)
    {
        var sourceFile = opts.Source ?? opts.PositionalArg1;
        var target = opts.Target;
        var testFile = opts.TestFile;
        var reportPath = opts.Report ?? "edge-cases.json";

        if (sourceFile is null || !File.Exists(sourceFile))
        {
            Console.Error.WriteLine("Usage: SlopEvaluator edge-cases --source <file.cs> [--test-file <tests.cs>]");
            return 1;
        }

        PrintHeader("EDGE CASE ANALYSIS");
        Console.WriteLine($"  Source: {sourceFile}");
        if (testFile is not null)
            Console.WriteLine($"  Tests:  {testFile}");

        var metrics = CreateMetrics(opts);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var analyzer = new EdgeCaseAnalyzer(sourceFile, target, testFile);
        var report = analyzer.Analyze();
        sw.Stop();
        metrics?.RecordSessionStart("edge-cases", sourceFile, target, 0);
        metrics?.RecordPhase("edge-cases", sw.Elapsed.TotalMilliseconds, true);

        Console.WriteLine();
        Console.WriteLine($"  Total edge cases: {report.EdgeCases.Count}");
        foreach (var group in report.CategorySummary.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {group.Key}: {group.Value}");

        var uncovered = report.EdgeCases
            .Where(e => e.CoveredByExistingTests != true)
            .OrderByDescending(e => e.RiskLevel == "high" ? 3 : e.RiskLevel == "medium" ? 2 : 1)
            .ToList();

        if (uncovered.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  Potentially uncovered ({uncovered.Count}):");
            foreach (var ec in uncovered.Take(10))
            {
                var risk = ec.RiskLevel switch { "high" => "\ud83d\udd34", "medium" => "\ud83d\udfe1", _ => "\ud83d\udfe2" };
                Console.WriteLine($"    {risk} {ec.Id}: {ec.Description}");
                Console.WriteLine($"       \u2192 {ec.SuggestedTestName}");
            }
            if (uncovered.Count > 10)
                Console.WriteLine($"    ... and {uncovered.Count - 10} more");
        }

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(reportPath, json);

        var metricsPath = Path.ChangeExtension(reportPath, ".metrics.jsonl");
        await FlushMetrics(metrics, metricsPath);

        Console.WriteLine($"\n  Report: {reportPath}");
        if (metrics is not null)
            Console.WriteLine($"  Metrics: {metricsPath}");

        return uncovered.Count > 0 ? 1 : 0;
    }

    internal static async Task<int> RunQualityAsync(CliOptions opts)
    {
        var configPath = opts.Config ?? opts.PositionalArg1;

        if (configPath is null || !File.Exists(configPath))
        {
            Console.Error.WriteLine("Usage: SlopEvaluator quality --config <mutations.json>");
            Console.Error.WriteLine("\nRuns: coverage \u2192 edge cases \u2192 mutations \u2192 combined report");
            return 1;
        }

        var config = ReportSerializer.LoadConfig(configPath);
        config = ApplyOverrides(config, opts);

        var existingCoverage = opts.CoverageFile;
        var reportPath = opts.Report ?? "quality-report.json";

        PrintHeader("FULL QUALITY ANALYSIS");
        Console.WriteLine($"  Config:  {configPath}");
        Console.WriteLine($"  Source:  {config.SourceFile}");
        Console.WriteLine($"  Stages:  " +
            $"{(opts.NoCoverage ? "\u2b1c" : "\u2705")} Coverage  " +
            $"{(opts.NoEdgeCases ? "\u2b1c" : "\u2705")} Edge Cases  " +
            $"{(opts.NoMutations ? "\u2b1c" : "\u2705")} Mutations");

        var metrics = CreateMetrics(opts);
        var qualityLogger = opts.LoggerFactory?.CreateLogger<QualityEngine>();
        var engine = new QualityEngine(config, metrics: metrics, logger: qualityLogger);
        var report = await engine.RunAsync(
            runCoverage: !opts.NoCoverage,
            runEdgeCases: !opts.NoEdgeCases,
            runMutations: !opts.NoMutations,
            existingCoverageReport: existingCoverage);

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(reportPath, json);

        var htmlPath = Path.ChangeExtension(reportPath, ".html");
        await HtmlReportWriter.WriteQualityHtmlAsync(report, htmlPath);

        var metricsPath = Path.ChangeExtension(reportPath, ".metrics.jsonl");
        await FlushMetrics(metrics, metricsPath);

        Console.WriteLine();
        Console.WriteLine($"  Reports: {reportPath}, {htmlPath}");
        if (metrics is not null)
            Console.WriteLine($"  Metrics: {metricsPath}");
        Console.WriteLine($"  Feed {reportPath} to Claude for targeted test generation.");

        return report.Scores.CompositeScore < 80 ? 1 : 0;
    }
}
