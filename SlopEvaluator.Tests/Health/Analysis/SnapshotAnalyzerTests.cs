using SlopEvaluator.Health.Analysis;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Analysis;

public class SnapshotAnalyzerTests
{
    private readonly SnapshotAnalyzer _analyzer = new();

    [Fact]
    public void GetDimensionScores_ReturnsAll14Dimensions()
    {
        var codebase = TestHelpers.CreateCodebase();

        var scores = _analyzer.GetDimensionScores(codebase);

        Assert.Equal(14, scores.Count);
        Assert.Contains("CodeQuality", scores.Keys);
        Assert.Contains("Testing", scores.Keys);
        Assert.Contains("Dependencies", scores.Keys);
        Assert.Contains("Security", scores.Keys);
        Assert.Contains("Observability", scores.Keys);
        Assert.Contains("CiCd", scores.Keys);
        Assert.Contains("Documentation", scores.Keys);
        Assert.Contains("DevExperience", scores.Keys);
        Assert.Contains("Performance", scores.Keys);
        Assert.Contains("Requirements", scores.Keys);
        Assert.Contains("TeamProcess", scores.Keys);
        Assert.Contains("AIQuality", scores.Keys);
        Assert.Contains("Architecture", scores.Keys);
        Assert.Contains("Structure", scores.Keys);
    }

    [Fact]
    public void FindBiggestMovers_SortsByAbsoluteDelta()
    {
        var delta = new SnapshotDelta
        {
            PreviousSnapshotId = Guid.NewGuid(),
            TimeBetween = TimeSpan.FromHours(1),
            OverallScoreDelta = 0.05,
            DimensionDeltas = new Dictionary<string, double>
            {
                ["Testing"] = 0.1,
                ["Security"] = -0.2,
                ["Documentation"] = 0.005, // below threshold
                ["CodeQuality"] = 0.05
            },
            Improvements = ["Testing", "CodeQuality"],
            Regressions = ["Security"]
        };

        var movers = _analyzer.FindBiggestMovers(delta);

        // Security has abs delta 0.2 (largest), Testing 0.1, CodeQuality 0.05
        // Documentation 0.005 is filtered out (< 0.001 threshold)
        Assert.Equal("Security", movers[0].Dimension);
        Assert.Equal(-0.2, movers[0].Delta);
        Assert.Equal("Testing", movers[1].Dimension);
    }

    [Fact]
    public void FindBiggestMovers_FiltersSmallDeltas()
    {
        var delta = new SnapshotDelta
        {
            PreviousSnapshotId = Guid.NewGuid(),
            TimeBetween = TimeSpan.FromHours(1),
            OverallScoreDelta = 0.0,
            DimensionDeltas = new Dictionary<string, double>
            {
                ["Testing"] = 0.0005, // below 0.001 threshold
                ["Security"] = 0.0001,
            },
            Improvements = [],
            Regressions = []
        };

        var movers = _analyzer.FindBiggestMovers(delta);

        Assert.Empty(movers);
    }

    [Fact]
    public void ComputeTrends_WithMultipleSnapshots_ReturnsSortedByDate()
    {
        var snapshots = new List<CodebaseSnapshot>
        {
            CreateSnapshot(DateTime.UtcNow.AddDays(-2), 0.5),
            CreateSnapshot(DateTime.UtcNow, 0.7),
            CreateSnapshot(DateTime.UtcNow.AddDays(-1), 0.6),
        };

        var trends = _analyzer.ComputeTrends(snapshots);

        Assert.True(trends.ContainsKey("Overall"));
        Assert.Equal(3, trends["Overall"].Count);
        // Should be sorted by date
        Assert.True(trends["Overall"][0].Date < trends["Overall"][1].Date);
        Assert.True(trends["Overall"][1].Date < trends["Overall"][2].Date);
    }

    [Fact]
    public void GenerateInsights_WithTwoSnapshots_ReturnsInsights()
    {
        var snapshots = new List<CodebaseSnapshot>
        {
            CreateSnapshot(DateTime.UtcNow.AddDays(-1), 0.4),
            CreateSnapshot(DateTime.UtcNow, 0.6, new SnapshotDelta
            {
                PreviousSnapshotId = Guid.NewGuid(),
                TimeBetween = TimeSpan.FromDays(1),
                OverallScoreDelta = 0.2,
                DimensionDeltas = new Dictionary<string, double>
                {
                    ["Testing"] = 0.3,
                    ["Security"] = -0.1
                },
                Improvements = ["Testing"],
                Regressions = ["Security"]
            }),
        };

        var insights = _analyzer.GenerateInsights(snapshots);

        Assert.NotEmpty(insights);
        // Should contain improvement message since delta > 0.01
        Assert.Contains(insights, i => i.Contains("improved"));
    }

    [Fact]
    public void GenerateInsights_WithSingleSnapshot_ReturnsEmpty()
    {
        var snapshots = new List<CodebaseSnapshot>
        {
            CreateSnapshot(DateTime.UtcNow, 0.5)
        };

        var insights = _analyzer.GenerateInsights(snapshots);

        Assert.Empty(insights);
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
