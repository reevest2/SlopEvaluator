using SlopEvaluator.Health.Analysis;
using SlopEvaluator.Health.Models;
using SlopEvaluator.Health.Storage;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Aggregates raw PromptInteraction data from the InteractionStore
/// into an AIInteractionQuality score.
/// </summary>
public class AIInteractionCollector
{
    private readonly InteractionStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIInteractionCollector"/> class.
    /// </summary>
    /// <param name="store">Interaction store providing persisted prompt interaction data.</param>
    public AIInteractionCollector(InteractionStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Collect AI quality metrics for a specific domain or all domains.
    /// </summary>
    public async Task<AIInteractionQuality> CollectAsync(string? domain = null)
    {
        var interactions = await _store.LoadAllAsync(domain);
        return Collect(interactions);
    }

    /// <summary>
    /// Collect from an in-memory list (for testing).
    /// </summary>
    public static AIInteractionQuality Collect(List<PromptInteraction> interactions)
    {
        if (interactions.Count == 0)
            return EmptyResult();

        var scores = interactions.Select(i => i.EffectiveScore).ToArray();
        var efficiencies = interactions.Select(i => i.Efficiency).ToArray();

        // First-pass success: interactions that needed only 1 iteration
        int firstPassCount = interactions.Count(i => i.IterationCount == 1);
        double firstPassRate = (double)firstPassCount / interactions.Count;

        // Input leverage — which input dimensions correlate most with output
        var inputLeverage = TrendAnalyzer.ComputeInputLeverage(interactions);

        // Context leverage: correlation of ContextDensity with EffectiveScore
        double contextLeverage = inputLeverage.TryGetValue("ContextDensity", out var cl) ? cl : 0;

        // Domain signal effectiveness: strongest positive correlation among input dimensions
        double domainSignal = inputLeverage.Count > 0
            ? inputLeverage.Values.Max()
            : 0;

        // Improvement trend: compare first half avg score to second half
        double improvementTrend = ComputeImprovementTrend(interactions);

        // Category breakdown
        var categoryScores = TrendAnalyzer.ComputeCategoryScores(interactions);
        var categoryInteractions = interactions
            .GroupBy(i => i.TaskCategory)
            .ToDictionary(g => g.Key, g => g.Count());

        string mostEffective = categoryScores.Count > 0
            ? categoryScores.MaxBy(kv => kv.Value).Key
            : "N/A";
        string leastEffective = categoryScores.Count > 0
            ? categoryScores.MinBy(kv => kv.Value).Key
            : "N/A";

        // Top corrections — most common correction themes
        var topCorrections = interactions
            .SelectMany(i => i.Corrections)
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        // Score trend over time
        var scoreTrend = TrendAnalyzer.ComputeTrend(interactions);

        return new AIInteractionQuality
        {
            AverageEffectiveScore = scores.Average(),
            AverageEfficiency = efficiencies.Average(),
            FirstPassSuccessRate = firstPassRate,
            ContextLeverage = NormalizeCorrelation(contextLeverage),
            DomainSignalEffectiveness = NormalizeCorrelation(domainSignal),
            ImprovementTrend = improvementTrend,
            Usage = new AIUsageProfile
            {
                TotalInteractions = interactions.Count,
                AverageIterations = interactions.Average(i => i.IterationCount),
                InteractionsByCategory = categoryInteractions,
                MostEffectiveCategory = mostEffective,
                LeastEffectiveCategory = leastEffective,
                TokenEfficiencyOverall = efficiencies.Average(),
                TopCorrections = topCorrections
            },
            ScoreByCategory = categoryScores,
            InputLeverage = inputLeverage,
            ScoreTrend = scoreTrend
        };
    }

    /// <summary>
    /// Compare first half vs second half of interactions (by timestamp).
    /// Returns 0.0-1.0 where 0.5 = flat, >0.5 = improving, less than 0.5 = declining.
    /// </summary>
    internal static double ComputeImprovementTrend(List<PromptInteraction> interactions)
    {
        if (interactions.Count < 2)
            return 0.5; // neutral

        var ordered = interactions.OrderBy(i => i.Timestamp).ToList();
        int mid = ordered.Count / 2;

        double firstHalf = ordered.Take(mid).Average(i => i.EffectiveScore);
        double secondHalf = ordered.Skip(mid).Average(i => i.EffectiveScore);

        // Delta range: -1.0 to +1.0, map to 0.0-1.0
        double delta = secondHalf - firstHalf;
        return Math.Clamp(0.5 + delta, 0.0, 1.0);
    }

    /// <summary>
    /// Normalize a Pearson correlation (-1 to 1) into a 0-1 score.
    /// Strong positive = high, near zero = medium, strong negative = low.
    /// </summary>
    internal static double NormalizeCorrelation(double r)
    {
        return Math.Clamp((r + 1.0) / 2.0, 0.0, 1.0);
    }

    private static AIInteractionQuality EmptyResult() => new()
    {
        AverageEffectiveScore = 0,
        AverageEfficiency = 0,
        FirstPassSuccessRate = 0,
        ContextLeverage = 0.5,
        DomainSignalEffectiveness = 0.5,
        ImprovementTrend = 0.5,
        Usage = new AIUsageProfile
        {
            TotalInteractions = 0,
            AverageIterations = 0,
            InteractionsByCategory = new Dictionary<string, int>(),
            MostEffectiveCategory = "N/A",
            LeastEffectiveCategory = "N/A",
            TokenEfficiencyOverall = 0,
            TopCorrections = []
        },
        ScoreByCategory = new Dictionary<string, double>(),
        InputLeverage = new Dictionary<string, double>(),
        ScoreTrend = []
    };
}
