using SlopEvaluator.Mutations.Analysis;
namespace SlopEvaluator.Mutations.Models;

// ═══════════════════════════════════════════════════════════════
//  TOKEN EFFICIENCY — measures ROI of AI-driven mutation testing
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Top-level report produced by the efficiency command.
/// </summary>
public sealed class TokenEfficiencyReport
{
    public required string GeneratedAt { get; init; }
    public string? Target { get; init; }
    public required List<EfficiencyEntry> Entries { get; init; }
    public DiminishingReturnsCurve? Curve { get; init; }
    public required EfficiencySummary Summary { get; init; }
    public required List<EfficiencyRecommendation> Recommendations { get; init; }
}

/// <summary>
/// One data point: a single agent round or mutation run with token cost and value.
/// </summary>
public sealed class EfficiencyEntry
{
    public required DateTime Timestamp { get; init; }
    public required string Target { get; init; }
    public int Round { get; init; }
    public string? SessionId { get; init; }

    // Cost side
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public double TokenCost { get; init; }

    // Value side
    public double MutationScoreBefore { get; init; }
    public double MutationScoreAfter { get; init; }
    public double ScoreDelta { get; init; }
    public int SurvivorsKilled { get; init; }
    public double Value { get; init; }

    // Efficiency = Value / Cost
    public double Efficiency { get; init; }
}

/// <summary>
/// Exponential decay curve fitted to marginal value per round.
/// Model: V(r) = A * e^(-Lambda * r)
/// </summary>
public sealed class DiminishingReturnsCurve
{
    /// <summary>Initial amplitude (value at round 0).</summary>
    public required double A { get; init; }

    /// <summary>Decay rate. Higher = faster diminishing returns.</summary>
    public required double Lambda { get; init; }

    /// <summary>Goodness of fit (0-1, higher is better).</summary>
    public required double RSquared { get; init; }

    /// <summary>Round where marginal value drops below threshold.</summary>
    public required int OptimalStopRound { get; init; }

    /// <summary>Number of data points used for the fit.</summary>
    public required int DataPoints { get; init; }
}

/// <summary>
/// Aggregate statistics across all efficiency entries.
/// </summary>
public sealed class EfficiencySummary
{
    public long TotalTokensSpent { get; init; }
    public double TotalCostEstimate { get; init; }
    public double TotalValueGenerated { get; init; }
    public double OverallEfficiency { get; init; }
    public int BestRound { get; init; }
    public int WorstRound { get; init; }
    public double AverageEfficiency { get; init; }
    public int TotalRounds { get; init; }
}

/// <summary>
/// Actionable recommendation based on efficiency analysis.
/// </summary>
public sealed class EfficiencyRecommendation
{
    /// <summary>"stop", "continue", "schedule", "batch-size", "more-data"</summary>
    public required string Type { get; init; }
    public required string Description { get; init; }
    public required double Confidence { get; init; }
}

/// <summary>
/// Options for the efficiency analysis.
/// </summary>
public sealed class TokenEfficiencyOptions
{
    public double InputPricePerMTok { get; init; } = ModelPricing.DefaultInputPrice;
    public double OutputPricePerMTok { get; init; } = ModelPricing.DefaultOutputPrice;
    public double StopThreshold { get; init; } = 0.1;
    public string? Target { get; init; }
}

/// <summary>
/// Token pricing helper. Defaults approximate Claude Sonnet pricing.
/// </summary>
public static class ModelPricing
{
    public const double DefaultInputPrice = 3.0;   // $ per 1M input tokens
    public const double DefaultOutputPrice = 15.0;  // $ per 1M output tokens

    public static double ComputeCost(int inputTokens, int outputTokens,
        double inputPrice = DefaultInputPrice, double outputPrice = DefaultOutputPrice)
    {
        return inputTokens * inputPrice / 1_000_000.0
             + outputTokens * outputPrice / 1_000_000.0;
    }
}
