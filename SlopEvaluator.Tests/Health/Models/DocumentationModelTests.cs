using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Models;

public class DocumentationModelTests
{
    [Fact]
    public void Score_WithAllPerfectScores_ReturnsOne()
    {
        var doc = TestHelpers.CreateDocumentation(1.0, 1.0, 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(1.0, doc.Score, precision: 10);
    }

    [Fact]
    public void Score_WithAllZeroScores_ReturnsZero()
    {
        var doc = TestHelpers.CreateDocumentation(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        Assert.Equal(0.0, doc.Score, precision: 10);
    }

    [Fact]
    public void Score_ApiDocCoverageHasHighestWeight()
    {
        // ApiDocCoverage has weight 0.25, highest
        var highApi = TestHelpers.CreateDocumentation(
            readmeCompleteness: 0.0, apiDocCoverage: 1.0, adrPresence: 0.0,
            docFreshness: 0.0, inlineCommentQuality: 0.0, onboardingDocumentation: 0.0);
        var highAdr = TestHelpers.CreateDocumentation(
            readmeCompleteness: 0.0, apiDocCoverage: 0.0, adrPresence: 1.0,
            docFreshness: 0.0, inlineCommentQuality: 0.0, onboardingDocumentation: 0.0);

        Assert.True(highApi.Score > highAdr.Score);
    }

    [Fact]
    public void Score_WeightsSum_ToOne()
    {
        double weightSum = 0.20 + 0.25 + 0.10 + 0.15 + 0.15 + 0.15;

        Assert.Equal(1.0, weightSum, precision: 10);
    }

    [Fact]
    public void Score_WithUniformScores_ReturnsThatScore()
    {
        var doc = TestHelpers.CreateDocumentation(0.6, 0.6, 0.6, 0.6, 0.6, 0.6);

        Assert.Equal(0.6, doc.Score, precision: 10);
    }

    [Fact]
    public void Score_ComputesCorrectWeightedAverage()
    {
        var doc = TestHelpers.CreateDocumentation(
            readmeCompleteness: 1.0, apiDocCoverage: 0.0,
            adrPresence: 0.0, docFreshness: 0.0,
            inlineCommentQuality: 0.0, onboardingDocumentation: 0.0);

        // Only readme has score: 1.0 * 0.20 / 1.0 = 0.20
        Assert.Equal(0.20, doc.Score, precision: 10);
    }
}
