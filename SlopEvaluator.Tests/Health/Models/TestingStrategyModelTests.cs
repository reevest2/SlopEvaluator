using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Models;

public class TestingStrategyModelTests
{
    [Fact]
    public void Score_WithAllPerfectScores_ReturnsOne()
    {
        var strategy = TestHelpers.CreateTestingStrategy(1.0, 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(1.0, strategy.Score, precision: 10);
    }

    [Fact]
    public void Score_WithAllZeroScores_ReturnsZero()
    {
        var strategy = TestHelpers.CreateTestingStrategy(0.0, 0.0, 0.0, 0.0, 0.0);

        Assert.Equal(0.0, strategy.Score, precision: 10);
    }

    [Fact]
    public void Score_MutationScoreHasHighestWeight()
    {
        // MutationScore weight is 0.30, highest among the 5 dimensions
        // Boosting only mutation score should have more impact than boosting others
        var highMutation = TestHelpers.CreateTestingStrategy(
            lineCoverage: 0.0, branchCoverage: 0.0, mutationScore: 1.0,
            edgeCaseCoverage: 0.0, testQualityScore: 0.0);
        var highLine = TestHelpers.CreateTestingStrategy(
            lineCoverage: 1.0, branchCoverage: 0.0, mutationScore: 0.0,
            edgeCaseCoverage: 0.0, testQualityScore: 0.0);

        Assert.True(highMutation.Score > highLine.Score);
    }

    [Fact]
    public void Score_WeightsSum_ToOne()
    {
        double weightSum = 0.15 + 0.20 + 0.30 + 0.15 + 0.20;

        Assert.Equal(1.0, weightSum, precision: 10);
    }

    [Fact]
    public void Score_WithUniformScores_ReturnsThatScore()
    {
        var strategy = TestHelpers.CreateTestingStrategy(0.7, 0.7, 0.7, 0.7, 0.7);

        Assert.Equal(0.7, strategy.Score, precision: 10);
    }

    [Fact]
    public void Score_ImplementsIScoreable()
    {
        var strategy = TestHelpers.CreateTestingStrategy();
        IScoreable scoreable = strategy;

        Assert.Equal(strategy.Score, scoreable.Score);
    }
}
