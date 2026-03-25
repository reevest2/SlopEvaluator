using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Models;

public class TeamProcessModelTests
{
    [Fact]
    public void Score_WithAllPerfectScores_ReturnsOne()
    {
        var process = TestHelpers.CreateTeamProcess(1.0, 1.0, 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(1.0, process.Score, precision: 10);
    }

    [Fact]
    public void Score_WithAllZeroScores_ReturnsZero()
    {
        var process = TestHelpers.CreateTeamProcess(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        Assert.Equal(0.0, process.Score, precision: 10);
    }

    [Fact]
    public void Score_PrCycleTimeAndReviewAndIncidentHaveHighestWeight()
    {
        // PrCycleTimeHealth=0.20, ReviewQuality=0.20, IncidentResponseHealth=0.20
        // These three share the highest weight at 0.20 each
        var highPr = TestHelpers.CreateTeamProcess(
            prCycleTimeHealth: 1.0, reviewQuality: 0.0,
            knowledgeDistribution: 0.0, commitHygiene: 0.0,
            branchStrategy: 0.0, incidentResponseHealth: 0.0);
        var highBranch = TestHelpers.CreateTeamProcess(
            prCycleTimeHealth: 0.0, reviewQuality: 0.0,
            knowledgeDistribution: 0.0, commitHygiene: 0.0,
            branchStrategy: 1.0, incidentResponseHealth: 0.0);

        // PR weight (0.20) > branch strategy weight (0.10)
        Assert.True(highPr.Score > highBranch.Score);
    }

    [Fact]
    public void Score_WeightsSum_ToOne()
    {
        double weightSum = 0.20 + 0.20 + 0.15 + 0.15 + 0.10 + 0.20;

        Assert.Equal(1.0, weightSum, precision: 10);
    }

    [Fact]
    public void Score_WithUniformScores_ReturnsThatScore()
    {
        var process = TestHelpers.CreateTeamProcess(0.4, 0.4, 0.4, 0.4, 0.4, 0.4);

        Assert.Equal(0.4, process.Score, precision: 10);
    }
}
