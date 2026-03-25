using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Shared;

public class ScoreAggregatorTests
{
    [Fact]
    public void WeightedAverage_WithEqualWeights_ReturnsSimpleAverage()
    {
        var result = ScoreAggregator.WeightedAverage(
            (0.6, 1.0), (0.8, 1.0), (1.0, 1.0));

        Assert.Equal(0.8, result, precision: 10);
    }

    [Fact]
    public void WeightedAverage_WithEmptyInput_ReturnsZero()
    {
        var result = ScoreAggregator.WeightedAverage();

        Assert.Equal(0.0, result);
    }

    [Fact]
    public void WeightedAverage_WithSingleItem_ReturnsThatScore()
    {
        var result = ScoreAggregator.WeightedAverage((0.75, 1.0));

        Assert.Equal(0.75, result, precision: 10);
    }

    [Fact]
    public void WeightedAverage_WithDifferentWeights_WeightsCorrectly()
    {
        // score=1.0 weight=3.0 contributes 3.0
        // score=0.0 weight=1.0 contributes 0.0
        // total = 3.0 / 4.0 = 0.75
        var result = ScoreAggregator.WeightedAverage(
            (1.0, 3.0), (0.0, 1.0));

        Assert.Equal(0.75, result, precision: 10);
    }

    [Fact]
    public void WeightedAverage_WithAllZeroWeights_ReturnsZero()
    {
        var result = ScoreAggregator.WeightedAverage(
            (0.5, 0.0), (0.8, 0.0));

        Assert.Equal(0.0, result);
    }

    [Fact]
    public void WeightedAverage_WithAllPerfectScores_ReturnsOne()
    {
        var result = ScoreAggregator.WeightedAverage(
            (1.0, 0.15), (1.0, 0.20), (1.0, 0.30),
            (1.0, 0.15), (1.0, 0.20));

        Assert.Equal(1.0, result, precision: 10);
    }

    [Fact]
    public void WeightedAverage_WithAllZeroScores_ReturnsZero()
    {
        var result = ScoreAggregator.WeightedAverage(
            (0.0, 0.15), (0.0, 0.20), (0.0, 0.30));

        Assert.Equal(0.0, result, precision: 10);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void WeightedAverage_WithUniformScores_ReturnsThatScore(double score)
    {
        var result = ScoreAggregator.WeightedAverage(
            (score, 0.3), (score, 0.7));

        Assert.Equal(score, result, precision: 10);
    }
}
