using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Reorders mutation candidates based on historical survival rates
/// and method complexity. Higher survival + higher complexity = highest priority.
/// </summary>
public sealed class MutationPrioritizer
{
    /// <summary>
    /// Reorder candidates so strategies with higher historical survival rates
    /// come first. Unknown strategies get medium priority (50% assumed survival).
    /// </summary>
    public IReadOnlyList<MutationCandidate> Prioritize(
        IReadOnlyList<MutationCandidate> candidates,
        TrendReport? history)
    {
        if (history is null || history.Entries.Count == 0)
            return candidates;

        var survivalRates = ComputeSurvivalRates(history);

        return candidates
            .OrderByDescending(c => survivalRates.GetValueOrDefault(c.Strategy, 0.5))
            .ThenBy(c => c.LineNumber)
            .ToList();
    }

    /// <summary>
    /// Reorder candidates using both survival rates AND method complexity.
    /// Score = survivalRate * 0.6 + normalizedCC * 0.4
    /// High-complexity methods with high survival = highest priority (test gaps in hard code).
    /// </summary>
    public IReadOnlyList<MutationCandidate> PrioritizeWithComplexity(
        IReadOnlyList<MutationCandidate> candidates,
        TrendReport? history,
        IReadOnlyList<MethodComplexity>? complexity)
    {
        if (candidates.Count == 0)
            return candidates;

        var survivalRates = history is not null && history.Entries.Count > 0
            ? ComputeSurvivalRates(history)
            : new Dictionary<string, double>();

        // Build a line-range → CC lookup from complexity data
        var ccByLine = BuildComplexityLookup(complexity);
        var maxCC = complexity?.Count > 0 ? complexity.Max(m => m.CyclomaticComplexity) : 1;

        return candidates
            .OrderByDescending(c =>
            {
                var survival = survivalRates.GetValueOrDefault(c.Strategy, 0.5);
                var cc = GetComplexityForLine(ccByLine, c.LineNumber);
                var normalizedCC = maxCC > 1 ? (double)cc / maxCC : 0.5;
                return survival * 0.6 + normalizedCC * 0.4;
            })
            .ThenBy(c => c.LineNumber)
            .ToList();
    }

    private static Dictionary<(int start, int end), int> BuildComplexityLookup(
        IReadOnlyList<MethodComplexity>? complexity)
    {
        if (complexity is null || complexity.Count == 0)
            return new();

        var lookup = new Dictionary<(int start, int end), int>();
        foreach (var m in complexity)
        {
            // Approximate method span: lineNumber to lineNumber + lineCount
            lookup[(m.LineNumber, m.LineNumber + m.LineCount)] = m.CyclomaticComplexity;
        }
        return lookup;
    }

    private static int GetComplexityForLine(
        Dictionary<(int start, int end), int> ccByLine, int lineNumber)
    {
        foreach (var ((start, end), cc) in ccByLine)
        {
            if (lineNumber >= start && lineNumber <= end)
                return cc;
        }
        return 1; // default: simple method
    }

    /// <summary>
    /// Compute survival rate per strategy from trend history.
    /// Survival rate = survived / (killed + survived). Higher = more test gaps.
    /// </summary>
    public static Dictionary<string, double> ComputeSurvivalRates(TrendReport history)
    {
        var aggregated = new Dictionary<string, (int killed, int survived)>();

        foreach (var entry in history.Entries)
        {
            if (entry.StrategyResults is null) continue;

            foreach (var (strategy, outcome) in entry.StrategyResults)
            {
                if (!aggregated.TryGetValue(strategy, out var agg))
                    agg = (0, 0);

                aggregated[strategy] = (agg.killed + outcome.Killed, agg.survived + outcome.Survived);
            }
        }

        return aggregated.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var total = kv.Value.killed + kv.Value.survived;
                return total == 0 ? 0.5 : (double)kv.Value.survived / total;
            });
    }
}
