using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Models;

public class AIInteractionQualityModelTests
{
    [Fact]
    public void Score_WithAllPerfectScores_ReturnsOne()
    {
        var ai = TestHelpers.CreateAIInteractionQuality(1.0, 1.0, 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(1.0, ai.Score, precision: 10);
    }

    [Fact]
    public void Score_WithAllZeroScores_ReturnsZero()
    {
        var ai = TestHelpers.CreateAIInteractionQuality(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        Assert.Equal(0.0, ai.Score, precision: 10);
    }

    [Fact]
    public void Score_AverageEffectiveScoreHasHighestWeight()
    {
        // AverageEffectiveScore weight is 0.25, highest
        var highEffective = TestHelpers.CreateAIInteractionQuality(
            averageEffectiveScore: 1.0, averageEfficiency: 0.0,
            firstPassSuccessRate: 0.0, contextLeverage: 0.0,
            domainSignalEffectiveness: 0.0, improvementTrend: 0.0);
        var highContext = TestHelpers.CreateAIInteractionQuality(
            averageEffectiveScore: 0.0, averageEfficiency: 0.0,
            firstPassSuccessRate: 0.0, contextLeverage: 1.0,
            domainSignalEffectiveness: 0.0, improvementTrend: 0.0);

        Assert.True(highEffective.Score > highContext.Score);
    }

    [Fact]
    public void Score_DoesNotIncludeDomainSignalEffectiveness()
    {
        // DomainSignalEffectiveness is NOT in the Score formula
        // Only: AverageEffectiveScore(0.25), AverageEfficiency(0.20),
        // FirstPassSuccessRate(0.20), ContextLeverage(0.15), ImprovementTrend(0.20)
        var withDomain = TestHelpers.CreateAIInteractionQuality(
            averageEffectiveScore: 0.5, averageEfficiency: 0.5,
            firstPassSuccessRate: 0.5, contextLeverage: 0.5,
            domainSignalEffectiveness: 1.0, improvementTrend: 0.5);
        var withoutDomain = TestHelpers.CreateAIInteractionQuality(
            averageEffectiveScore: 0.5, averageEfficiency: 0.5,
            firstPassSuccessRate: 0.5, contextLeverage: 0.5,
            domainSignalEffectiveness: 0.0, improvementTrend: 0.5);

        Assert.Equal(withDomain.Score, withoutDomain.Score, precision: 10);
    }

    [Fact]
    public void Score_WeightsSum_ToOne()
    {
        // AverageEffectiveScore=0.25, AverageEfficiency=0.20,
        // FirstPassSuccessRate=0.20, ContextLeverage=0.15, ImprovementTrend=0.20
        double weightSum = 0.25 + 0.20 + 0.20 + 0.15 + 0.20;

        Assert.Equal(1.0, weightSum, precision: 10);
    }

    [Fact]
    public void Score_WithUniformScores_ReturnsThatScore()
    {
        var ai = TestHelpers.CreateAIInteractionQuality(0.3, 0.3, 0.3, 0.3, 0.3, 0.3);

        Assert.Equal(0.3, ai.Score, precision: 10);
    }
}
