namespace SlopEvaluator.Health.Models;

/// <summary>
/// A single recorded interaction: prompt in, output out, scored.
/// The atomic unit of measurement in the probability model.
/// </summary>
public class PromptInteraction
{
    /// <summary>Unique identifier for this interaction.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp when the interaction was recorded.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Domain or project this interaction is scoped to (e.g. "DemonsAndDogs").</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Task category (e.g. "ComponentGen", "Refactor", "Architecture", "Debug").</summary>
    public string TaskCategory { get; set; } = string.Empty;

    // --- INPUT VECTOR (what you controlled) ---

    /// <summary>Input signal dimensions describing what levers were pulled.</summary>
    public InputSignal Input { get; set; } = new();

    // --- OUTPUT EVALUATION (what you measured) ---

    /// <summary>Output score dimensions for the AI response quality.</summary>
    public OutputScore Output { get; set; } = new();

    // --- RAW DATA ---

    /// <summary>Brief description of the prompt (not the full prompt text).</summary>
    public string PromptSummary { get; set; } = string.Empty;

    /// <summary>Brief description of what the AI actually produced.</summary>
    public string OutputSummary { get; set; } = string.Empty;

    /// <summary>Desired outcome, if different from what was produced.</summary>
    public string? DesiredOutcome { get; set; }

    /// <summary>Corrections or redirections applied to the AI output.</summary>
    public List<string> Corrections { get; set; } = [];

    // --- ITERATION TRACKING ---

    /// <summary>Number of turns needed to reach an acceptable result.</summary>
    public int IterationCount { get; set; } = 1;

    /// <summary>Links follow-up prompts to the original interaction.</summary>
    public Guid? ParentInteractionId { get; set; }

    // --- DERIVED ---

    /// <summary>Weighted output score from 0.0 (worst) to 1.0 (best).</summary>
    public double EffectiveScore => Output.ComputeWeightedScore();

    /// <summary>Effective score divided by iteration count.</summary>
    public double Efficiency => IterationCount > 0 ? EffectiveScore / IterationCount : 0;

    /// <summary>Freeform tags for filtering and grouping.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>Optional free-text notes about the interaction.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// The input dimensions — each one is a lever you pulled (or didn't).
/// Scored 0.0 to 1.0 representing how much signal you provided.
/// </summary>
public class InputSignal
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for context density provided.</summary>
    public double ContextDensity { get; set; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for constraint specificity.</summary>
    public double ConstraintSpecificity { get; set; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for exemplar anchoring provided.</summary>
    public double ExemplarAnchoring { get; set; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for domain signal strength.</summary>
    public double DomainSignalStrength { get; set; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for prompt precision.</summary>
    public double PromptPrecision { get; set; }

    /// <summary>
    /// Computes a composite signal from all input dimensions using optional weights.
    /// </summary>
    /// <param name="weights">Optional weights for each dimension; uses equal weights if null or wrong length.</param>
    /// <returns>Weighted average of all input dimensions.</returns>
    public double CompositeSignal(double[]? weights = null)
    {
        double[] values = [ContextDensity, ConstraintSpecificity, ExemplarAnchoring, DomainSignalStrength, PromptPrecision];

        if (weights is null || weights.Length != values.Length)
            return values.Average();

        double total = 0;
        double weightSum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i] * weights[i];
            weightSum += weights[i];
        }
        return weightSum > 0 ? total / weightSum : 0;
    }
}

/// <summary>
/// How you scored what came back. Each dimension is 0.0 to 1.0.
/// </summary>
public class OutputScore
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for first-pass usability.</summary>
    public double FirstPassUsability { get; set; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for domain alignment.</summary>
    public double DomainAlignment { get; set; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for structural correctness.</summary>
    public double StructuralCorrectness { get; set; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for signal-to-noise ratio.</summary>
    public double SignalToNoise { get; set; }

    /// <summary>
    /// Computes a weighted score across all output dimensions.
    /// </summary>
    /// <returns>Weighted average from 0.0 (worst) to 1.0 (best).</returns>
    public double ComputeWeightedScore()
    {
        return (FirstPassUsability * 0.35)
             + (StructuralCorrectness * 0.25)
             + (DomainAlignment * 0.25)
             + (SignalToNoise * 0.15);
    }
}

/// <summary>
/// Aggregated view over a set of interactions. This is where patterns surface.
/// </summary>
public class DomainInsight
{
    /// <summary>Domain name these insights are aggregated for.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Total number of interactions in this domain.</summary>
    public int TotalInteractions { get; set; }

    /// <summary>Average efficiency (score per iteration) across interactions.</summary>
    public double AverageEfficiency { get; set; }

    /// <summary>Average effective score across interactions.</summary>
    public double AverageScore { get; set; }

    /// <summary>Average number of iterations per interaction.</summary>
    public double AverageIterations { get; set; }

    /// <summary>Per-input-dimension leverage ratios showing which inputs drive output quality.</summary>
    public Dictionary<string, double> InputLeverage { get; set; } = [];

    /// <summary>Average scores keyed by task category.</summary>
    public Dictionary<string, double> CategoryScores { get; set; } = [];

    /// <summary>Score trend over time for visualizing improvement.</summary>
    public List<TrendPoint> ScoreTrend { get; set; } = [];
}

/// <summary>
/// A single data point in a time-series score trend.
/// </summary>
public class TrendPoint
{
    /// <summary>Date of this trend data point.</summary>
    public DateTime Date { get; set; }

    /// <summary>Average score for interactions on this date.</summary>
    public double AverageScore { get; set; }

    /// <summary>Number of interactions on this date.</summary>
    public int Count { get; set; }
}
