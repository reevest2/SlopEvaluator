namespace SlopEvaluator.Shared.Scoring;

/// <summary>Utility for computing weighted averages of scored dimensions.</summary>
public static class ScoreAggregator
{
    public static double WeightedAverage(params (double score, double weight)[] items)
    {
        double totalWeight = 0, totalScore = 0;
        foreach (var (score, weight) in items)
        {
            totalScore += score * weight;
            totalWeight += weight;
        }
        return totalWeight > 0 ? totalScore / totalWeight : 0;
    }
}
