namespace SlopEvaluator.Health.Models;

/// <summary>
/// Every scoreable dimension implements this.
/// </summary>
public interface IScoreable
{
    /// <summary>
    /// Normalized 0.0–1.0 health score for this dimension.
    /// </summary>
    double Score { get; }
}

/// <summary>
/// How a data point was collected.
/// </summary>
public enum CollectionMethod
{
    /// <summary>Fully automated collection via tooling.</summary>
    Automated,
    /// <summary>Manually entered measurement.</summary>
    Manual,
    /// <summary>Combination of automated and manual collection.</summary>
    Hybrid,
    /// <summary>Inferred from other data points.</summary>
    Inferred
}

/// <summary>
/// A single scored dimension with metadata about how it was measured.
/// </summary>
public sealed record ScoredDimension
{
    /// <summary>Display name of the dimension.</summary>
    public required string Name { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best).</summary>
    public required double Value { get; init; }

    /// <summary>How this data point was collected.</summary>
    public required CollectionMethod Collection { get; init; }

    /// <summary>When this dimension was measured, if known.</summary>
    public DateTime? MeasuredAt { get; init; }

    /// <summary>Tool or process that produced this measurement.</summary>
    public string? Source { get; init; }
}

/// <summary>
/// Weighted average helper used by all composite scores.
/// </summary>
public static class ScoreAggregator
{
    /// <summary>
    /// Computes the weighted average of the given score-weight pairs.
    /// </summary>
    /// <param name="items">Pairs of (score, weight) to average.</param>
    /// <returns>Weighted average, or 0 if total weight is zero.</returns>
    public static double WeightedAverage(params (double score, double weight)[] items)
    {
        double totalWeight = 0;
        double total = 0;
        for (int i = 0; i < items.Length; i++)
        {
            total += items[i].score * items[i].weight;
            totalWeight += items[i].weight;
        }
        return totalWeight > 0 ? total / totalWeight : 0;
    }
}
