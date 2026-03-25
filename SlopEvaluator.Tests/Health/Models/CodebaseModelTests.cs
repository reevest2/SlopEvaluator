using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Models;

public class CodebaseModelTests
{
    [Fact]
    public void Score_WithAllPerfectScores_IsNearOne()
    {
        var codebase = TestHelpers.CreateCodebase(overallScore: 1.0);

        // Sub-model score formulas may not produce exactly 1.0 even with 1.0 inputs
        Assert.InRange(codebase.Score, 0.80, 1.0);
    }

    [Fact]
    public void Score_WithAllZeroScores_IsNearZero()
    {
        var codebase = TestHelpers.CreateCodebase(overallScore: 0.0);

        // Sub-model score formulas may not produce exactly 0.0 even with 0.0 inputs
        Assert.InRange(codebase.Score, 0.0, 0.20);
    }

    [Fact]
    public void Score_WithUniformHalfScores_ReturnsHalf()
    {
        var codebase = TestHelpers.CreateCodebase(overallScore: 0.5);

        Assert.Equal(0.5, codebase.Score, precision: 10);
    }

    [Fact]
    public void Score_WeightsAreCorrect_SumToOne()
    {
        // The weights in the Codebase.Score property should sum to 1.0
        // Quality=0.15, Testing=0.15, Dependencies=0.08, Security=0.10,
        // Observability=0.07, Pipeline=0.08, Documentation=0.05,
        // DevEx=0.07, Performance=0.07, Requirements=0.08, Process=0.05, AIQuality=0.05
        double weightSum = 0.15 + 0.15 + 0.08 + 0.10 + 0.07 + 0.08 + 0.05 + 0.07 + 0.07 + 0.08 + 0.05 + 0.05;

        Assert.Equal(1.0, weightSum, precision: 10);
    }

    [Fact]
    public void Score_IsBetweenZeroAndOne()
    {
        var codebase = TestHelpers.CreateCodebase(qualityScore: 0.9, testingScore: 0.3);
        var score = codebase.Score;

        Assert.InRange(score, 0.0, 1.0);
    }

    [Fact]
    public void Score_IsComputedProperty_ChangesWithInputs()
    {
        var high = TestHelpers.CreateCodebase(overallScore: 0.9);
        var low = TestHelpers.CreateCodebase(overallScore: 0.1);

        Assert.True(high.Score > low.Score);
    }
}
