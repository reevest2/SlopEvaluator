namespace SlopEvaluator.Health.Models;

/// <summary>
/// Aggregated AI interaction quality — wraps existing PromptInteraction data
/// into scoreable dimensions for the Codebase model.
/// </summary>
public sealed record AIInteractionQuality : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for average effective output score.</summary>
    public required double AverageEffectiveScore { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for average efficiency (score per iteration).</summary>
    public required double AverageEfficiency { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for first-pass success rate.</summary>
    public required double FirstPassSuccessRate { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for context leverage effectiveness.</summary>
    public required double ContextLeverage { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for domain signal effectiveness.</summary>
    public required double DomainSignalEffectiveness { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for improvement trend over time.</summary>
    public required double ImprovementTrend { get; init; }

    /// <summary>AI usage profile with interaction counts and category breakdown.</summary>
    public required AIUsageProfile Usage { get; init; }

    /// <summary>Average scores keyed by task category.</summary>
    public required Dictionary<string, double> ScoreByCategory { get; init; }

    /// <summary>Per-input-dimension leverage ratios showing which inputs drive output quality.</summary>
    public required Dictionary<string, double> InputLeverage { get; init; }

    /// <summary>Score trend over time for visualizing improvement.</summary>
    public required List<TrendPoint> ScoreTrend { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (AverageEffectiveScore, 0.25),
        (AverageEfficiency, 0.20),
        (FirstPassSuccessRate, 0.20),
        (ContextLeverage, 0.15),
        (ImprovementTrend, 0.20)
    );
}

/// <summary>
/// AI usage summary: interaction counts, categories, and efficiency.
/// </summary>
public sealed record AIUsageProfile
{
    /// <summary>Total number of AI interactions recorded.</summary>
    public required int TotalInteractions { get; init; }

    /// <summary>Average number of iterations per interaction.</summary>
    public required double AverageIterations { get; init; }

    /// <summary>Interaction counts grouped by task category.</summary>
    public required Dictionary<string, int> InteractionsByCategory { get; init; }

    /// <summary>Category with the highest average effective score.</summary>
    public required string MostEffectiveCategory { get; init; }

    /// <summary>Category with the lowest average effective score.</summary>
    public required string LeastEffectiveCategory { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for overall token efficiency.</summary>
    public required double TokenEfficiencyOverall { get; init; }

    /// <summary>Most common corrections applied to AI outputs.</summary>
    public required List<string> TopCorrections { get; init; }
}
