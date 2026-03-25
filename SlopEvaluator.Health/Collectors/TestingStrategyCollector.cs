using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Bridges MutationHarness report data into TestingStrategy scores.
/// Reads mutation-report.json, coverage-report.json, test-quality-report.json,
/// and token-efficiency-report.json. Also runs dotnet test for suite profiling.
/// </summary>
public class TestingStrategyCollector
{
    private readonly ILogger<TestingStrategyCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestingStrategyCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public TestingStrategyCollector(ILogger<TestingStrategyCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<TestingStrategyCollector>.Instance;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Collect testing strategy by scanning for MutationHarness reports and running dotnet test.
    /// </summary>
    public async Task<TestingStrategy> CollectAsync(string projectPath)
    {
        // Find all MutationHarness reports
        _logger.LogInformation("Scanning for test reports in {Path}", projectPath);
        var mutationReports = await FindAndParseMutationReportsAsync(projectPath);
        _logger.LogInformation("Found {Count} mutation report(s)", mutationReports.Count);
        var coverageReport = await FindAndParseCoverageReportAsync(projectPath);
        var qualityReport = await FindAndParseQualityReportAsync(projectPath);
        var testQualityReport = await FindAndParseTestQualityReportAsync(projectPath);
        var efficiencyReport = await FindAndParseEfficiencyReportAsync(projectPath);

        // Run dotnet test to get test suite info
        var suiteProfile = await ProfileTestSuiteAsync(projectPath);

        // Aggregate mutation scores across all reports
        var mutationProfile = AggregateMutationReports(mutationReports);

        // Coverage from quality report or coverage report.
        // Quality report is preferred because it includes edge-case coverage;
        // fall back to basic coverage report if quality report is unavailable.
        // All report values are percentages (0-100), normalized here to 0.0-1.0.
        double lineCoverage = 0;
        double branchCoverage = 0;
        double edgeCaseCoverage = 0;

        if (qualityReport is not null)
        {
            lineCoverage = qualityReport.LineCoverage / 100.0;
            branchCoverage = qualityReport.BranchCoverage / 100.0;
            edgeCaseCoverage = qualityReport.EdgeCaseCoverage / 100.0;
        }
        else if (coverageReport is not null)
        {
            // Basic coverage report lacks edge-case data; leave edgeCaseCoverage at 0
            lineCoverage = coverageReport.LineCoverage / 100.0;
            branchCoverage = coverageReport.BranchCoverage / 100.0;
        }

        // Test quality from report
        var qualityProfile = BuildTestQualityProfile(testQualityReport);

        // Token efficiency
        var efficiencyProfile = BuildEfficiencyProfile(efficiencyReport);

        _logger.LogDebug("Testing coverage: line={LineCoverage:F1}%, branch={BranchCoverage:F1}%, mutation={MutationScore:F3}", lineCoverage * 100, branchCoverage * 100, mutationProfile.MutationScore);

        // Mutation score is the ratio of killed mutants to total valid mutants (killed + survived).
        // A higher score means the test suite is better at detecting injected faults.
        double mutationScore = mutationProfile.MutationScore;

        // Test quality score is normalized from the pillar-based grading system (0-48 max)
        // to a 0.0-1.0 range. Returns 0 when no test quality report is available.
        double testQualityScore = testQualityReport is not null
            ? testQualityReport.TotalScore / testQualityReport.MaxScore
            : 0;

        return new TestingStrategy
        {
            LineCoverage = lineCoverage,
            BranchCoverage = branchCoverage,
            MutationScore = mutationScore,
            EdgeCaseCoverage = edgeCaseCoverage,
            TestQualityScore = testQualityScore,
            Suite = suiteProfile,
            Quality = qualityProfile,
            MutationTesting = mutationProfile,
            Efficiency = efficiencyProfile
        };
    }

    // --- Report Discovery and Parsing ---

    private async Task<List<MutationReportData>> FindAndParseMutationReportsAsync(string path)
    {
        var reports = new List<MutationReportData>();
        var files = Directory.GetFiles(path, "*mutation-report*.json", SearchOption.AllDirectories)
            .Where(f => {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
                    && !rel.Contains(".claude/worktrees/");
            })
            .ToList();

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var report = JsonSerializer.Deserialize<MutationReportData>(json, JsonOptions);
                if (report is not null)
                    reports.Add(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse mutation report at {Path}", file);
            }
        }

        return reports;
    }

    private static async Task<CoverageReportData?> FindAndParseCoverageReportAsync(string path)
    {
        var file = FindFirstReport(path, "*coverage-report*.json");
        if (file is null) return null;
        try
        {
            var json = await File.ReadAllTextAsync(file);
            return JsonSerializer.Deserialize<CoverageReportData>(json, JsonOptions);
        }
        catch { return null; }
    }

    private static async Task<QualityScoresData?> FindAndParseQualityReportAsync(string path)
    {
        var file = FindFirstReport(path, "*quality-report*.json");
        if (file is null) return null;
        try
        {
            var json = await File.ReadAllTextAsync(file);
            var report = JsonSerializer.Deserialize<QualityReportData>(json, JsonOptions);
            return report?.Scores;
        }
        catch { return null; }
    }

    private static async Task<TestQualityReportData?> FindAndParseTestQualityReportAsync(string path)
    {
        var file = FindFirstReport(path, "*test-pillar*.json");
        if (file is null) return null;
        try
        {
            var json = await File.ReadAllTextAsync(file);
            return JsonSerializer.Deserialize<TestQualityReportData>(json, JsonOptions);
        }
        catch { return null; }
    }

    private static async Task<EfficiencyReportData?> FindAndParseEfficiencyReportAsync(string path)
    {
        var file = FindFirstReport(path, "*efficiency*.json");
        if (file is null) return null;
        try
        {
            var json = await File.ReadAllTextAsync(file);
            return JsonSerializer.Deserialize<EfficiencyReportData>(json, JsonOptions);
        }
        catch { return null; }
    }

    private static string? FindFirstReport(string path, string pattern) =>
        Directory.GetFiles(path, pattern, SearchOption.AllDirectories)
            .Where(f => {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
                    && !rel.Contains(".claude/worktrees/");
            })
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();

    // --- Test Suite Profiling ---

    private static async Task<TestSuiteProfile> ProfileTestSuiteAsync(string path)
    {
        int totalTests = 0;
        TimeSpan totalRunTime = TimeSpan.Zero;
        string framework = "unknown";

        // Count test projects and detect framework
        var testProjects = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)
            .Where(f => {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                return rel.Contains("Test")
                    && !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains(".claude/worktrees/");
            })
            .ToList();

        foreach (var proj in testProjects.Take(3))
        {
            var content = await File.ReadAllTextAsync(proj);
            if (content.Contains("xunit")) framework = "xUnit";
            else if (content.Contains("nunit")) framework = "NUnit";
            else if (content.Contains("mstest")) framework = "MSTest";
        }

        // Run dotnet test with list to count
        try
        {
            var sw = Stopwatch.StartNew();
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "test --no-build --list-tests",
                WorkingDirectory = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is not null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                sw.Stop();
                totalRunTime = sw.Elapsed;

                // Count test lines (lines after "The following Tests are available:")
                var lines = output.Split('\n');
                bool counting = false;
                foreach (var line in lines)
                {
                    if (line.Contains("The following Tests are available"))
                    { counting = true; continue; }
                    if (counting && line.Trim().Length > 0)
                        totalTests++;
                }
            }
        }
        catch { /* dotnet test unavailable */ }

        // Count test files to estimate unit vs integration vs e2e
        var testFiles = Directory.GetFiles(path, "*Tests.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(path, "*Test.cs", SearchOption.AllDirectories))
            .Where(f => {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains(".claude/worktrees/");
            })
            .ToList();

        int unitFiles = testFiles.Count(f => !f.Contains("Integration") && !f.Contains("E2E") && !f.Contains("EndToEnd"));
        int integrationFiles = testFiles.Count(f => f.Contains("Integration"));
        int e2eFiles = testFiles.Count(f => f.Contains("E2E") || f.Contains("EndToEnd") || f.Contains("Playwright"));

        // Estimate test counts from file counts if dotnet test couldn't enumerate
        if (totalTests == 0)
            totalTests = testFiles.Count * 5; // rough estimate: ~5 tests per file

        int totalFileTests = unitFiles + integrationFiles + e2eFiles;
        double unitRatio = totalFileTests > 0 ? (double)unitFiles / totalFileTests : 0.8;
        double integrationRatio = totalFileTests > 0 ? (double)integrationFiles / totalFileTests : 0.15;
        double e2eRatio = totalFileTests > 0 ? (double)e2eFiles / totalFileTests : 0.05;

        // Count source files for test-to-code ratio
        int sourceFiles = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
            .Count(f => {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
                    && !rel.Contains("Test")
                    && !rel.Contains(".claude/worktrees/");
            });

        return new TestSuiteProfile
        {
            TotalTests = totalTests,
            UnitTests = (int)(totalTests * unitRatio),
            IntegrationTests = (int)(totalTests * integrationRatio),
            EndToEndTests = (int)(totalTests * e2eRatio),
            TestToCodeRatio = sourceFiles > 0 ? (double)testFiles.Count / sourceFiles : 0,
            TotalRunTime = totalRunTime,
            AverageTestDuration = totalTests > 0 ? totalRunTime / totalTests : TimeSpan.Zero,
            FlakyTests = 0,
            Determinism = 1.0,
            Framework = framework,
            TestCategories = new List<string> { "Unit", "Integration", "E2E" }
                .Where((_, i) => new[] { unitFiles, integrationFiles, e2eFiles }[i] > 0)
                .ToList()
        };
    }

    // --- Aggregation ---

    // Aggregates multiple mutation reports (one per source file) into a single profile.
    // Compile errors are excluded from the kill ratio because they don't test behavior.
    internal static MutationTestingProfile AggregateMutationReports(List<MutationReportData> reports)
    {
        if (reports.Count == 0)
            return new MutationTestingProfile
            {
                MutationScore = 0, TotalMutants = 0, Killed = 0, Survived = 0,
                CompileErrors = 0, ScoreByStrategy = [], SurvivorPatterns = [],
                MutationsPerMinute = 0
            };

        int totalKilled = 0, totalSurvived = 0, totalCompileErrors = 0;
        double totalDurationMinutes = 0;
        // Track per-strategy kill rates to identify weak mutation strategies
        var strategyKilled = new Dictionary<string, int>();
        var strategyTotal = new Dictionary<string, int>();
        var survivorDescriptions = new List<string>();

        foreach (var report in reports)
        {
            totalDurationMinutes += report.TotalDuration.TotalMinutes;

            foreach (var result in report.Results)
            {
                var strategy = result.Strategy ?? "unknown";

                if (!strategyTotal.ContainsKey(strategy)) strategyTotal[strategy] = 0;
                if (!strategyKilled.ContainsKey(strategy)) strategyKilled[strategy] = 0;

                switch (result.Outcome?.ToLowerInvariant())
                {
                    case "killed":
                        totalKilled++;
                        strategyKilled[strategy]++;
                        strategyTotal[strategy]++;
                        break;
                    case "survived":
                        totalSurvived++;
                        strategyTotal[strategy]++;
                        // Collect survivor descriptions to surface recurring weak spots
                        survivorDescriptions.Add(result.Description ?? "unknown");
                        break;
                    case "compileerror":
                        // Compile errors are tracked but excluded from the kill ratio
                        totalCompileErrors++;
                        break;
                }
            }
        }

        // Mutation score = killed / (killed + survived). Compile errors excluded.
        int totalValid = totalKilled + totalSurvived;
        double mutationScore = totalValid > 0 ? (double)totalKilled / totalValid : 0;

        var scoreByStrategy = strategyTotal
            .Where(kv => kv.Value > 0)
            .ToDictionary(
                kv => kv.Key,
                kv => strategyKilled.GetValueOrDefault(kv.Key) / (double)kv.Value);

        // Find common survivor patterns
        var survivorPatterns = survivorDescriptions
            .GroupBy(d => d)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        int totalMutants = totalKilled + totalSurvived + totalCompileErrors;
        double mutationsPerMinute = totalDurationMinutes > 0 ? totalMutants / totalDurationMinutes : 0;

        return new MutationTestingProfile
        {
            MutationScore = mutationScore,
            TotalMutants = totalMutants,
            Killed = totalKilled,
            Survived = totalSurvived,
            CompileErrors = totalCompileErrors,
            ScoreByStrategy = scoreByStrategy,
            SurvivorPatterns = survivorPatterns,
            MutationsPerMinute = mutationsPerMinute
        };
    }

    private static TestQualityProfile BuildTestQualityProfile(TestQualityReportData? report)
    {
        if (report is null)
            return new TestQualityProfile
            {
                Trustworthiness = 0, Precision = 0, Maintainability = 0, DiagnosticValue = 0,
                AttributeScores = [], RSquared = 0, CalibratedWeights = [], TopIssues = []
            };

        var pillars = report.Pillars ?? new Dictionary<string, PillarData>();

        double GetPillarScore(string name) =>
            pillars.TryGetValue(name, out var p) && p.MaxScore > 0 ? p.Score / p.MaxScore : 0;

        // Flatten pillar attribute scores into a single dictionary.
        // Each attribute is scored 0-3 in the test quality report; normalize to 0.0-1.0.
        var attrScores = new Dictionary<string, double>();
        foreach (var pillar in pillars)
        {
            if (pillar.Value.Attributes is not null)
            {
                foreach (var attr in pillar.Value.Attributes)
                    attrScores[attr.Key] = attr.Value / 3.0;
            }
        }

        var topIssues = report.Issues?
            .OrderBy(i => i.Score)
            .Take(10)
            .Select(i => $"{i.TestName}: {i.Description}")
            .ToList() ?? [];

        return new TestQualityProfile
        {
            Trustworthiness = GetPillarScore("Trustworthiness"),
            Precision = GetPillarScore("Precision"),
            Maintainability = GetPillarScore("Maintainability"),
            DiagnosticValue = GetPillarScore("Diagnostic Value"),
            AttributeScores = attrScores,
            RSquared = 0, // populated from WeightProfile if available
            CalibratedWeights = [],
            TopIssues = topIssues
        };
    }

    private static TokenEfficiencyProfile BuildEfficiencyProfile(EfficiencyReportData? report)
    {
        if (report?.Summary is null)
            return new TokenEfficiencyProfile
            {
                TotalTokensSpent = 0, TotalCostEstimate = 0, OverallEfficiency = 0,
                OptimalStopRound = 0, DiminishingReturnsLambda = 0, Recommendations = []
            };

        return new TokenEfficiencyProfile
        {
            TotalTokensSpent = report.Summary.TotalTokensSpent,
            TotalCostEstimate = report.Summary.TotalCostEstimate,
            OverallEfficiency = report.Summary.OverallEfficiency,
            OptimalStopRound = report.Curve?.OptimalStopRound ?? 0,
            DiminishingReturnsLambda = report.Curve?.Lambda ?? 0,
            Recommendations = report.Recommendations?
                .Select(r => r.Description)
                .ToList() ?? []
        };
    }

    // --- Deserialization Models (internal, match MutationHarness JSON) ---

    internal class MutationReportData
    {
        public string SourceFile { get; set; } = "";
        public string Target { get; set; } = "";
        public DateTime RunDate { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan BaselineDuration { get; set; }
        public int BaselineTestCount { get; set; }
        public List<MutationResultData> Results { get; set; } = [];
    }

    internal class MutationResultData
    {
        public string Id { get; set; } = "";
        public string? Strategy { get; set; }
        public string? Description { get; set; }
        public string? RiskLevel { get; set; }
        public string? Outcome { get; set; }
        public TimeSpan Duration { get; set; }
        public string? OriginalCode { get; set; }
        public string? MutatedCode { get; set; }
        public int? LineNumberHint { get; set; }
        public int TestsRun { get; set; }
        public int TestsFailed { get; set; }
    }

    internal class CoverageReportData
    {
        public double LineCoverage { get; set; }
        public double BranchCoverage { get; set; }
        public int LinesValid { get; set; }
        public int LinesCovered { get; set; }
    }

    internal class QualityReportData
    {
        public QualityScoresData? Scores { get; set; }
    }

    internal class QualityScoresData
    {
        public double LineCoverage { get; set; }
        public double BranchCoverage { get; set; }
        public double MutationScore { get; set; }
        public double EdgeCaseCoverage { get; set; }
    }

    internal class TestQualityReportData
    {
        public string TestFile { get; set; } = "";
        public int TestsAnalyzed { get; set; }
        public double TotalScore { get; set; }
        public double MaxScore { get; set; } = 48;
        public string? Grade { get; set; }
        public Dictionary<string, PillarData>? Pillars { get; set; }
        public List<TestIssueData>? Issues { get; set; }
    }

    internal class PillarData
    {
        public string Name { get; set; } = "";
        public double Score { get; set; }
        public double MaxScore { get; set; } = 12;
        public Dictionary<string, double>? Attributes { get; set; }
    }

    internal class TestIssueData
    {
        public string TestName { get; set; } = "";
        public string Attribute { get; set; } = "";
        public string Description { get; set; } = "";
        public int Score { get; set; }
    }

    internal class EfficiencyReportData
    {
        public List<EfficiencyEntryData>? Entries { get; set; }
        public CurveData? Curve { get; set; }
        public SummaryData? Summary { get; set; }
        public List<RecommendationData>? Recommendations { get; set; }
    }

    internal class EfficiencyEntryData
    {
        public int Round { get; set; }
        public double MutationScoreBefore { get; set; }
        public double MutationScoreAfter { get; set; }
        public double ScoreDelta { get; set; }
    }

    internal class CurveData
    {
        public double Lambda { get; set; }
        public double RSquared { get; set; }
        public int OptimalStopRound { get; set; }
    }

    internal class SummaryData
    {
        public long TotalTokensSpent { get; set; }
        public double TotalCostEstimate { get; set; }
        public double OverallEfficiency { get; set; }
    }

    internal class RecommendationData
    {
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public double Confidence { get; set; }
    }
}
