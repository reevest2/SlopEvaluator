using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Analysis;

/// <summary>
/// Computes trends and insights from recorded interactions.
/// </summary>
public static class TrendAnalyzer
{
    /// <summary>
    /// Build a DomainInsight from a set of interactions, all assumed to be the same domain.
    /// </summary>
    public static DomainInsight Analyze(string domain, List<PromptInteraction> interactions)
    {
        if (interactions.Count == 0)
            return new DomainInsight { Domain = domain };

        var insight = new DomainInsight
        {
            Domain = domain,
            TotalInteractions = interactions.Count,
            AverageScore = interactions.Average(i => i.EffectiveScore),
            AverageEfficiency = interactions.Average(i => i.Efficiency),
            AverageIterations = interactions.Average(i => i.IterationCount),
            InputLeverage = ComputeInputLeverage(interactions),
            CategoryScores = ComputeCategoryScores(interactions),
            ScoreTrend = ComputeTrend(interactions)
        };

        return insight;
    }

    /// <summary>
    /// Correlation between each input dimension and effective score.
    /// Positive = that lever helps. Near zero = doesn't matter. Negative = hurts (unlikely but possible).
    /// </summary>
    public static Dictionary<string, double> ComputeInputLeverage(List<PromptInteraction> interactions)
    {
        if (interactions.Count < 2)
            return new Dictionary<string, double>();

        var scores = interactions.Select(i => i.EffectiveScore).ToArray();

        return new Dictionary<string, double>
        {
            ["ContextDensity"] = Correlation(interactions.Select(i => i.Input.ContextDensity).ToArray(), scores),
            ["ConstraintSpecificity"] = Correlation(interactions.Select(i => i.Input.ConstraintSpecificity).ToArray(), scores),
            ["ExemplarAnchoring"] = Correlation(interactions.Select(i => i.Input.ExemplarAnchoring).ToArray(), scores),
            ["DomainSignalStrength"] = Correlation(interactions.Select(i => i.Input.DomainSignalStrength).ToArray(), scores),
            ["PromptPrecision"] = Correlation(interactions.Select(i => i.Input.PromptPrecision).ToArray(), scores)
        };
    }

    /// <summary>
    /// Compute average effective score per task category.
    /// </summary>
    public static Dictionary<string, double> ComputeCategoryScores(List<PromptInteraction> interactions)
    {
        return interactions
            .GroupBy(i => i.TaskCategory)
            .ToDictionary(g => g.Key, g => g.Average(i => i.EffectiveScore));
    }

    /// <summary>
    /// Compute daily score trend from a set of interactions.
    /// </summary>
    public static List<TrendPoint> ComputeTrend(List<PromptInteraction> interactions)
    {
        return interactions
            .OrderBy(i => i.Timestamp)
            .GroupBy(i => i.Timestamp.Date)
            .Select(g => new TrendPoint
            {
                Date = g.Key,
                AverageScore = g.Average(i => i.EffectiveScore),
                Count = g.Count()
            })
            .ToList();
    }

    /// <summary>
    /// Pearson correlation coefficient between two arrays.
    /// </summary>
    public static double Correlation(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2)
            return 0;

        double n = x.Length;
        double sumX = x.Sum(), sumY = y.Sum();
        double sumXY = x.Zip(y, (a, b) => a * b).Sum();
        double sumX2 = x.Sum(v => v * v);
        double sumY2 = y.Sum(v => v * v);

        double numerator = n * sumXY - sumX * sumY;
        double denominator = Math.Sqrt((n * sumX2 - sumX * sumX) * (n * sumY2 - sumY * sumY));

        return denominator == 0 ? 0 : numerator / denominator;
    }
}
