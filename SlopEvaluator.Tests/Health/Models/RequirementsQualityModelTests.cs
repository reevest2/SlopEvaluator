using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Models;

public class RequirementsQualityModelTests
{
    [Fact]
    public void Score_WithAllPerfectScores_ReturnsOne()
    {
        var req = TestHelpers.CreateRequirementsQuality(1.0, 1.0, 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(1.0, req.Score, precision: 10);
    }

    [Fact]
    public void Score_WithAllZeroScores_ReturnsZero()
    {
        var req = TestHelpers.CreateRequirementsQuality(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        Assert.Equal(0.0, req.Score, precision: 10);
    }

    [Fact]
    public void Score_TestabilityHasHighestWeight()
    {
        // Testability weight is 0.25, highest
        var highTestability = TestHelpers.CreateRequirementsQuality(
            clarity: 0.0, completeness: 0.0, testability: 1.0,
            atomicity: 0.0, acceptanceCriteriaQuality: 0.0, traceabilityToCode: 0.0);
        var highClarity = TestHelpers.CreateRequirementsQuality(
            clarity: 1.0, completeness: 0.0, testability: 0.0,
            atomicity: 0.0, acceptanceCriteriaQuality: 0.0, traceabilityToCode: 0.0);

        Assert.True(highTestability.Score > highClarity.Score);
    }

    [Fact]
    public void Score_WeightsSum_ToOne()
    {
        // Clarity=0.20, Completeness=0.20, Testability=0.25,
        // Atomicity=0.10, AcceptanceCriteria=0.15, Traceability=0.10
        double weightSum = 0.20 + 0.20 + 0.25 + 0.10 + 0.15 + 0.10;

        Assert.Equal(1.0, weightSum, precision: 10);
    }

    [Fact]
    public void Score_ComputesCorrectWeightedAverage()
    {
        // Only testability=1.0, everything else 0.0
        // Expected: 1.0 * 0.25 / 1.0 = 0.25
        var req = TestHelpers.CreateRequirementsQuality(
            clarity: 0.0, completeness: 0.0, testability: 1.0,
            atomicity: 0.0, acceptanceCriteriaQuality: 0.0, traceabilityToCode: 0.0);

        Assert.Equal(0.25, req.Score, precision: 10);
    }
}
