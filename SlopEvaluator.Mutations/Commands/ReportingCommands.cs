using System.Text.Json;
using SlopEvaluator.Mutations.Models;
using static SlopEvaluator.Mutations.Commands.CommandHelpers;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Engine;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Commands;

internal static class ReportingCommands
{
    internal static int RunTrend(CliOptions opts)
    {
        var historyPath = opts.History ?? ".mutation-history.jsonl";
        var target = opts.Target;

        if (!File.Exists(historyPath))
        {
            Console.Error.WriteLine($"No history file found at: {historyPath}");
            Console.Error.WriteLine("Run mutation tests first \u2014 history is recorded automatically.");
            return 1;
        }

        PrintHeader("MUTATION SCORE TREND");

        var report = TrendAnalyzer.Analyze(historyPath, target);

        Console.WriteLine($"  {report.Summary}");
        Console.WriteLine();

        if (report.Entries.Count > 0)
        {
            Console.WriteLine("  History:");
            foreach (var entry in report.Entries.TakeLast(20))
            {
                var bar = new string('\u2588', (int)(entry.MutationScore / 5));
                var icon = entry.MutationScore >= 80 ? "\u2705" : entry.MutationScore >= 60 ? "\ud83d\udfe1" : "\ud83d\udd34";
                Console.WriteLine($"    {entry.Timestamp:yyyy-MM-dd HH:mm} {icon} {entry.MutationScore,5:F1}% {bar} ({entry.Killed}K/{entry.Survived}S)");
            }
        }

        return 0;
    }

    internal static int RunTestQuality(CliOptions opts)
    {
        var testFile = opts.TestFile;
        var testProject = opts.TestProject;
        var weightsPath = opts.Weights;

        var files = new List<string>();
        if (testFile is not null)
        {
            files.Add(Path.GetFullPath(testFile));
        }
        else if (testProject is not null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(testProject)) ?? ".";
            files.AddRange(Directory.GetFiles(dir, "*Tests.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(dir, "*Test.cs", SearchOption.AllDirectories)));
        }
        else if (opts.PositionalArg1 is not null && File.Exists(opts.PositionalArg1))
        {
            files.Add(Path.GetFullPath(opts.PositionalArg1));
        }
        else
        {
            Console.Error.WriteLine("Usage: mutation-harness test-quality --test-file <file.cs>");
            Console.Error.WriteLine("       mutation-harness test-quality --test-project <tests.csproj>");
            return 1;
        }

        WeightProfile? weights = null;
        if (weightsPath is not null && File.Exists(weightsPath))
        {
            var json = File.ReadAllText(weightsPath);
            weights = JsonSerializer.Deserialize<WeightProfile>(json, JsonOptions);
        }

        PrintHeader("TEST QUALITY ANALYSIS");
        Console.WriteLine($"  Files: {files.Count}");
        if (weights is not null)
            Console.WriteLine($"  Weights: {weightsPath} (R\u00b2={weights.RSquared:F2})");
        Console.WriteLine();

        var allIssues = new List<TestIssue>();
        var totalTests = 0;
        double totalScore = 0;

        foreach (var file in files)
        {
            var analyzer = new TestQualityAnalyzer(file, weights, Console.WriteLine);
            var report = analyzer.Analyze();

            if (report.TestsAnalyzed == 0) continue;

            totalTests += report.TestsAnalyzed;
            totalScore += report.TotalScore;

            var fileName = Path.GetFileName(file);
            Console.WriteLine($"  {fileName}  Grade: {report.Grade} ({report.TotalScore:F0}/48)  Tests: {report.TestsAnalyzed}");
            foreach (var (name, pillar) in report.Pillars)
            {
                var filled = (int)(pillar.Score / 12 * 20);
                var bar = new string('\u2588', filled) + new string('\u2591', 20 - filled);
                Console.WriteLine($"    {name,-20} {pillar.Score,4:F1}/12  [{bar}]");
            }

            if (report.Issues.Count > 0)
            {
                Console.WriteLine($"\n    Issues ({report.Issues.Count}):");
                foreach (var issue in report.Issues.Take(5))
                {
                    var smell = issue.Smell is not null ? $" [{issue.Smell}]" : "";
                    Console.WriteLine($"    \u26a0 {issue.TestName}{smell}: {issue.Description}");
                }
                if (report.Issues.Count > 5)
                    Console.WriteLine($"    ... and {report.Issues.Count - 5} more");
            }

            allIssues.AddRange(report.Issues);
            Console.WriteLine();
        }

        if (totalTests > 0)
        {
            var avgScore = totalScore / files.Count(f =>
            {
                var a = new TestQualityAnalyzer(f);
                return a.Analyze().TestsAnalyzed > 0;
            });
            var grade = TestQualityReport.ComputeGrade(avgScore);
            Console.WriteLine($"  Overall: Grade {grade} ({avgScore:F1}/48)  Tests: {totalTests}  Issues: {allIssues.Count}");
        }

        return 0;
    }

    internal static int RunCalibrate(CliOptions opts)
    {
        var testProject = opts.TestProject;
        var reportsDir = opts.MutationReports ?? ".";
        var outputPath = opts.Output ?? ".test-quality-weights.json";

        if (testProject is null)
        {
            Console.Error.WriteLine("Usage: mutation-harness calibrate --test-project <tests.csproj> --mutation-reports <dir>");
            return 1;
        }

        PrintHeader("WEIGHT CALIBRATION");

        var testDir = Path.GetDirectoryName(Path.GetFullPath(testProject)) ?? ".";
        var testFiles = Directory.GetFiles(testDir, "*Tests.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(testDir, "*Test.cs", SearchOption.AllDirectories))
            .Distinct()
            .ToList();

        var reportFiles = Directory.GetFiles(reportsDir, "*-report.json", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(reportsDir, "*.mutation-report.json", SearchOption.AllDirectories))
            .ToList();

        Console.WriteLine($"  Test files:      {testFiles.Count}");
        Console.WriteLine($"  Mutation reports: {reportFiles.Count}");

        var data = new List<CalibrationDataPoint>();

        foreach (var testFile in testFiles)
        {
            var analyzer = new TestQualityAnalyzer(testFile);
            var quality = analyzer.Analyze();
            if (quality.TestsAnalyzed == 0) continue;

            var avgScores = new double[QualityAttributes.Count];
            foreach (var ts in quality.TestScores)
            {
                for (int i = 0; i < QualityAttributes.Count; i++)
                    avgScores[i] += ts.AttributeScores[i];
            }
            for (int i = 0; i < QualityAttributes.Count; i++)
                avgScores[i] /= quality.TestScores.Count;

            var fileName = Path.GetFileNameWithoutExtension(testFile)
                .Replace("Tests", "").Replace("Test", "");

            var matchingReport = reportFiles.FirstOrDefault(r =>
                Path.GetFileName(r).Contains(fileName, StringComparison.OrdinalIgnoreCase));

            double mutScore = 50;
            if (matchingReport is not null)
            {
                try
                {
                    var json = File.ReadAllText(matchingReport);
                    var report = JsonSerializer.Deserialize<MutationReport>(json, JsonOptions);
                    if (report is not null)
                        mutScore = report.MutationScore;
                }
                catch { /* skip bad reports */ }
            }

            data.Add(new CalibrationDataPoint
            {
                TestFile = testFile,
                AttributeScores = avgScores,
                MutationScore = mutScore
            });
        }

        Console.WriteLine($"  Data points:     {data.Count}");

        if (data.Count < 3)
        {
            Console.Error.WriteLine("  Not enough data points (need 3+). Run more mutation tests first.");
            return 1;
        }

        var result = WeightCalibrator.Calibrate(data);

        Console.WriteLine($"  R\u00b2:              {result.Profile.RSquared:F4}");
        Console.WriteLine($"  MAE:             {result.MeanAbsoluteError:F2}%");
        Console.WriteLine();

        Console.WriteLine("  Learned weights:");
        var ranked = result.Profile.Weights
            .Select((w, i) => (Name: QualityAttributes.Names[i], Weight: w))
            .OrderByDescending(x => Math.Abs(x.Weight))
            .ToList();

        foreach (var (name, weight) in ranked)
        {
            var bar = new string('\u2588', (int)(Math.Abs(weight) * 5));
            Console.WriteLine($"    {name,-25} {weight,7:+0.00;-0.00}  {bar}");
        }

        Console.WriteLine();
        Console.WriteLine("  Insights:");
        foreach (var insight in result.Insights)
            Console.WriteLine($"    \u2022 {insight}");

        var outputJson = JsonSerializer.Serialize(result.Profile, JsonOptions);
        File.WriteAllText(outputPath, outputJson);
        Console.WriteLine($"\n  Saved: {outputPath}");

        return 0;
    }

    internal static int RunEfficiency(CliOptions opts)
    {
        var historyPath = opts.History ?? ".mutation-history.jsonl";
        var target = opts.Target;
        var inputPrice = double.TryParse(opts.InputPrice, out var ip)
            ? ip : ModelPricing.DefaultInputPrice;
        var outputPrice = double.TryParse(opts.OutputPrice, out var op)
            ? op : ModelPricing.DefaultOutputPrice;
        var threshold = double.TryParse(opts.Threshold, out var th)
            ? th : 0.1;
        var reportPath = opts.Report ?? "efficiency-report.json";

        if (!File.Exists(historyPath))
        {
            Console.Error.WriteLine($"No history file found at: {historyPath}");
            Console.Error.WriteLine("Run mutation tests first \u2014 history is recorded automatically.");
            return 1;
        }

        PrintHeader("TOKEN EFFICIENCY ANALYSIS");

        var options = new TokenEfficiencyOptions
        {
            InputPricePerMTok = inputPrice,
            OutputPricePerMTok = outputPrice,
            StopThreshold = threshold,
            Target = target
        };

        var report = TokenEfficiencyAnalyzer.Analyze(historyPath, options);

        Console.WriteLine($"  Rounds analyzed: {report.Summary.TotalRounds}");
        Console.WriteLine($"  Total tokens:    {report.Summary.TotalTokensSpent:N0}");
        Console.WriteLine($"  Est. cost:       ${report.Summary.TotalCostEstimate:F4}");
        Console.WriteLine($"  Total value:     {report.Summary.TotalValueGenerated:F1}");
        Console.WriteLine($"  Efficiency:      {report.Summary.OverallEfficiency:F2} value/$");
        Console.WriteLine($"  Avg efficiency:  {report.Summary.AverageEfficiency:F2} value/$");
        Console.WriteLine();

        if (report.Entries.Count > 0)
        {
            Console.WriteLine("  Round  Score\u0394    Value    Cost($)   Eff(v/$)  Target");
            Console.WriteLine("  \u2500\u2500\u2500\u2500\u2500  \u2500\u2500\u2500\u2500\u2500\u2500   \u2500\u2500\u2500\u2500\u2500    \u2500\u2500\u2500\u2500\u2500\u2500\u2500   \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500  \u2500\u2500\u2500\u2500\u2500\u2500");
            foreach (var e in report.Entries.TakeLast(20))
            {
                var deltaStr = e.ScoreDelta >= 0 ? $"+{e.ScoreDelta:F1}%" : $"{e.ScoreDelta:F1}%";
                Console.WriteLine($"  {e.Round,5}  {deltaStr,6}   {e.Value,5:F1}    {e.TokenCost,7:F4}   {e.Efficiency,8:F2}  {e.Target}");
            }
            Console.WriteLine();
        }

        if (report.Curve is not null && report.Curve.RSquared > 0)
        {
            Console.WriteLine($"  Diminishing Returns Curve: V(r) = {report.Curve.A:F2} \u00d7 e^(-{report.Curve.Lambda:F2} \u00d7 r)");
            Console.WriteLine($"  Fit quality (R\u00b2):         {report.Curve.RSquared:F3}");
            Console.WriteLine($"  Optimal stop round:        {report.Curve.OptimalStopRound}");
            Console.WriteLine();
        }

        if (report.Recommendations.Count > 0)
        {
            Console.WriteLine("  Recommendations:");
            foreach (var rec in report.Recommendations)
            {
                var conf = rec.Confidence >= 0.8 ? "\u2588\u2588\u2588" : rec.Confidence >= 0.5 ? "\u2588\u2588\u2591" : "\u2588\u2591\u2591";
                Console.WriteLine($"    [{rec.Type}] {conf} {rec.Description}");
            }
            Console.WriteLine();
        }

        var json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(reportPath, json);
        Console.WriteLine($"  Saved: {reportPath}");

        return 0;
    }
}
