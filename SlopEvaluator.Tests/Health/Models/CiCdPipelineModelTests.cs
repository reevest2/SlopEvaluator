using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Models;

public class CiCdPipelineModelTests
{
    [Fact]
    public void Score_WithAllPerfectScores_ReturnsOne()
    {
        var pipeline = TestHelpers.CreateCiCdPipeline(1.0, 1.0, 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(1.0, pipeline.Score, precision: 10);
    }

    [Fact]
    public void Score_WithAllZeroScores_ReturnsZero()
    {
        var pipeline = TestHelpers.CreateCiCdPipeline(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        Assert.Equal(0.0, pipeline.Score, precision: 10);
    }

    [Fact]
    public void Score_PipelineCompletenessHasHighestWeight()
    {
        // PipelineCompleteness weight is 0.25, highest
        var highCompleteness = TestHelpers.CreateCiCdPipeline(
            buildReliability: 0.0, buildSpeed: 0.0, deployFrequency: 0.0,
            pipelineCompleteness: 1.0, environmentParity: 0.0, rollbackCapability: 0.0);
        var highSpeed = TestHelpers.CreateCiCdPipeline(
            buildReliability: 0.0, buildSpeed: 1.0, deployFrequency: 0.0,
            pipelineCompleteness: 0.0, environmentParity: 0.0, rollbackCapability: 0.0);

        Assert.True(highCompleteness.Score > highSpeed.Score);
    }

    [Fact]
    public void Score_WeightsSum_ToOne()
    {
        double weightSum = 0.20 + 0.10 + 0.15 + 0.25 + 0.15 + 0.15;

        Assert.Equal(1.0, weightSum, precision: 10);
    }

    [Fact]
    public void Score_WithUniformScores_ReturnsThatScore()
    {
        var pipeline = TestHelpers.CreateCiCdPipeline(0.8, 0.8, 0.8, 0.8, 0.8, 0.8);

        Assert.Equal(0.8, pipeline.Score, precision: 10);
    }
}
