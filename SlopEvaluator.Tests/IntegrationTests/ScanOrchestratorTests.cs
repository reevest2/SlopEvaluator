using SlopEvaluator.Health.Models;
using SlopEvaluator.Health.Analysis;

namespace SlopEvaluator.Tests.IntegrationTests;

public class ScanOrchestratorTests
{
    [Fact]
    public void SnapshotAnalyzer_ComputeTrends_IntegrationWithCodebase()
    {
        // Integration test: create realistic snapshots and verify analysis works end-to-end
        var analyzer = new SnapshotAnalyzer();
        var snapshots = new List<CodebaseSnapshot>
        {
            CreateSnapshot(DateTime.UtcNow.AddDays(-3), 0.4),
            CreateSnapshot(DateTime.UtcNow.AddDays(-2), 0.5),
            CreateSnapshot(DateTime.UtcNow.AddDays(-1), 0.6),
            CreateSnapshot(DateTime.UtcNow, 0.7),
        };

        var trends = analyzer.ComputeTrends(snapshots);

        Assert.True(trends.ContainsKey("Overall"));
        Assert.Equal(4, trends["Overall"].Count);

        // All dimension keys should be present
        Assert.True(trends.ContainsKey("CodeQuality"));
        Assert.True(trends.ContainsKey("Testing"));
        Assert.True(trends.ContainsKey("Security"));
    }

    [Fact]
    public void CodebaseScore_EndToEnd_ProducesReasonableScore()
    {
        // Create a full codebase with varied scores and verify the composite works
        var codebase = new Codebase
        {
            Name = "IntegrationTest",
            RepositoryUrl = "https://github.com/test/repo",
            SnapshotDate = DateTime.UtcNow,
            Stack = new DotNetStack
            {
                TargetFramework = "net10.0", LanguageVersion = "13.0",
                NullableEnabled = true, ImplicitUsings = true,
                AotCompatible = false, SdkType = "Microsoft.NET.Sdk"
            },
            Architecture = TestHelpers.CreateArchitecture(0.8),
            Structure = TestHelpers.CreateProjectStructure(0.7),
            Quality = TestHelpers.CreateCodeQuality(0.85),
            Testing = TestHelpers.CreateTestingStrategy(0.7, 0.6, 0.8, 0.5, 0.7),
            Dependencies = TestHelpers.CreateDependencyHealth(0.9),
            Security = TestHelpers.CreateSecurityPosture(0.75),
            Observability = TestHelpers.CreateObservability(0.6),
            Pipeline = TestHelpers.CreateCiCdPipeline(0.8, 0.7, 0.6, 0.9, 0.7, 0.5),
            Documentation = TestHelpers.CreateDocumentation(0.5, 0.4, 0.3, 0.6, 0.5, 0.4),
            DevEx = TestHelpers.CreateDeveloperExperience(0.7, 0.8, 0.6, 0.7, 0.8, 0.7),
            Performance = TestHelpers.CreatePerformanceProfile(0.65),
            Requirements = TestHelpers.CreateRequirementsQuality(0.6, 0.5, 0.7, 0.5, 0.6, 0.4),
            Process = TestHelpers.CreateTeamProcess(0.7, 0.8, 0.6, 0.7, 0.5, 0.8),
            AIQuality = TestHelpers.CreateAIInteractionQuality(0.6, 0.5, 0.7, 0.4, 0.5, 0.6)
        };

        var score = codebase.Score;

        // With scores generally between 0.4-0.9, the weighted average should be reasonable
        Assert.InRange(score, 0.5, 0.9);
    }

    [Fact]
    public void SnapshotAnalyzer_GenerateInsights_WithThreeImprovingSnapshots()
    {
        var analyzer = new SnapshotAnalyzer();

        var snap1 = CreateSnapshot(DateTime.UtcNow.AddDays(-2), 0.4);
        var snap2 = CreateSnapshot(DateTime.UtcNow.AddDays(-1), 0.5,
            new SnapshotDelta
            {
                PreviousSnapshotId = snap1.Id,
                TimeBetween = TimeSpan.FromDays(1),
                OverallScoreDelta = 0.1,
                DimensionDeltas = new Dictionary<string, double>
                {
                    ["Testing"] = 0.15,
                    ["Security"] = 0.05
                },
                Improvements = ["Testing", "Security"],
                Regressions = []
            });
        var snap3 = CreateSnapshot(DateTime.UtcNow, 0.6,
            new SnapshotDelta
            {
                PreviousSnapshotId = snap2.Id,
                TimeBetween = TimeSpan.FromDays(1),
                OverallScoreDelta = 0.1,
                DimensionDeltas = new Dictionary<string, double>
                {
                    ["CodeQuality"] = 0.1,
                    ["Documentation"] = 0.05
                },
                Improvements = ["CodeQuality", "Documentation"],
                Regressions = []
            });

        var insights = analyzer.GenerateInsights([snap1, snap2, snap3]);

        Assert.NotEmpty(insights);
        // Should detect upward trend
        Assert.Contains(insights, i => i.Contains("upward trend"));
    }

    [Fact]
    public void TrendAnalyzer_Correlation_WithPerfectCorrelation_ReturnsOne()
    {
        double[] x = [1.0, 2.0, 3.0, 4.0, 5.0];
        double[] y = [2.0, 4.0, 6.0, 8.0, 10.0];

        var r = TrendAnalyzer.Correlation(x, y);

        Assert.Equal(1.0, r, precision: 10);
    }

    [Fact]
    public void TrendAnalyzer_Correlation_WithNegativeCorrelation_ReturnsMinusOne()
    {
        double[] x = [1.0, 2.0, 3.0, 4.0, 5.0];
        double[] y = [10.0, 8.0, 6.0, 4.0, 2.0];

        var r = TrendAnalyzer.Correlation(x, y);

        Assert.Equal(-1.0, r, precision: 10);
    }

    [Fact]
    public void TrendAnalyzer_Correlation_WithSingleElement_ReturnsZero()
    {
        double[] x = [1.0];
        double[] y = [2.0];

        var r = TrendAnalyzer.Correlation(x, y);

        Assert.Equal(0.0, r);
    }

    private static CodebaseSnapshot CreateSnapshot(
        DateTime takenAt, double score, SnapshotDelta? delta = null)
    {
        return new CodebaseSnapshot
        {
            Id = Guid.NewGuid(),
            CodebaseName = "TestCodebase",
            TakenAt = takenAt,
            GitCommitSha = "abc123",
            GitBranch = "main",
            Data = TestHelpers.CreateCodebase(overallScore: score),
            DeltaFromPrevious = delta
        };
    }
}
