using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Analysis;

/// <summary>
/// Analyzes trends across multiple codebase snapshots.
/// </summary>
public class SnapshotAnalyzer
{
    private readonly ILogger<SnapshotAnalyzer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public SnapshotAnalyzer(ILogger<SnapshotAnalyzer>? logger = null)
    {
        _logger = logger ?? NullLogger<SnapshotAnalyzer>.Instance;
    }

    private static readonly string[] DimensionNames =
    [
        "CodeQuality", "Testing", "Dependencies", "Security",
        "Observability", "CiCd", "Documentation", "DevExperience",
        "Performance", "Requirements", "TeamProcess", "AIQuality",
        "Architecture", "Structure"
    ];

    /// <summary>
    /// Compute score trend for each dimension across snapshots.
    /// </summary>
    public Dictionary<string, List<(DateTime Date, double Score)>> ComputeTrends(
        List<CodebaseSnapshot> snapshots)
    {
        _logger.LogInformation("Computing trends across {SnapshotCount} snapshots", snapshots.Count);
        var trends = new Dictionary<string, List<(DateTime, double)>>();
        foreach (var dim in DimensionNames)
            trends[dim] = [];

        trends["Overall"] = [];

        foreach (var snap in snapshots.OrderBy(s => s.TakenAt))
        {
            var scores = GetDimensionScores(snap.Data);
            foreach (var kv in scores)
                trends[kv.Key].Add((snap.TakenAt, kv.Value));
            trends["Overall"].Add((snap.TakenAt, snap.Data.Score));
        }

        return trends;
    }

    /// <summary>
    /// Find the dimensions that moved the most in a delta.
    /// </summary>
    public List<(string Dimension, double Delta)> FindBiggestMovers(SnapshotDelta delta)
    {
        return delta.DimensionDeltas
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .Where(kv => Math.Abs(kv.Value) > 0.001)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Compute Pearson correlation between every pair of dimensions across snapshots.
    /// Answers: "when dimension X improves, does dimension Y also improve?"
    /// </summary>
    public Dictionary<(string, string), double> ComputeCrossCorrelation(
        List<CodebaseSnapshot> snapshots)
    {
        if (snapshots.Count < 3) return [];

        var scoresByDim = new Dictionary<string, double[]>();
        var ordered = snapshots.OrderBy(s => s.TakenAt).ToList();

        foreach (var dim in DimensionNames)
        {
            scoresByDim[dim] = ordered
                .Select(s => GetDimensionScore(s.Data, dim))
                .ToArray();
        }

        var correlations = new Dictionary<(string, string), double>();
        for (int i = 0; i < DimensionNames.Length; i++)
        {
            for (int j = i + 1; j < DimensionNames.Length; j++)
            {
                var r = TrendAnalyzer.Correlation(
                    scoresByDim[DimensionNames[i]],
                    scoresByDim[DimensionNames[j]]);
                correlations[(DimensionNames[i], DimensionNames[j])] = r;
            }
        }

        return correlations;
    }

    /// <summary>
    /// Generate natural language insights from snapshot history.
    /// </summary>
    public List<string> GenerateInsights(List<CodebaseSnapshot> snapshots)
    {
        _logger.LogInformation("Generating insights from {SnapshotCount} snapshots", snapshots.Count);
        var insights = new List<string>();
        if (snapshots.Count < 2) return insights;

        var ordered = snapshots.OrderBy(s => s.TakenAt).ToList();
        var latest = ordered.Last();
        var previous = ordered[^2];

        if (latest.DeltaFromPrevious is not null)
        {
            var delta = latest.DeltaFromPrevious;

            if (delta.OverallScoreDelta > 0.01)
                insights.Add($"Overall health improved by {delta.OverallScoreDelta:+0.000} since last scan");
            else if (delta.OverallScoreDelta < -0.01)
                insights.Add($"Overall health regressed by {delta.OverallScoreDelta:0.000} since last scan");
            else
                insights.Add("Overall health is stable since last scan");

            var movers = FindBiggestMovers(delta);
            if (movers.Count > 0)
            {
                var top = movers.First();
                if (top.Delta > 0)
                    insights.Add($"Biggest improvement: {top.Dimension} (+{top.Delta:F3})");
                else
                    insights.Add($"Biggest regression: {top.Dimension} ({top.Delta:F3})");
            }

            if (delta.Improvements.Count > 0)
                insights.Add($"{delta.Improvements.Count} dimensions improved, {delta.Regressions.Count} regressed");
        }

        // Identify weakest dimensions
        var scores = GetDimensionScores(latest.Data);
        var weakest = scores.OrderBy(kv => kv.Value).First();
        var strongest = scores.OrderByDescending(kv => kv.Value).First();
        insights.Add($"Strongest: {strongest.Key} ({strongest.Value:F3}). Weakest: {weakest.Key} ({weakest.Value:F3})");

        // Trend direction
        if (ordered.Count >= 3)
        {
            var recentScores = ordered.TakeLast(3).Select(s => s.Data.Score).ToArray();
            if (recentScores[2] > recentScores[1] && recentScores[1] > recentScores[0])
            {
                _logger.LogInformation("Trend direction: upward (3 consecutive improvements)");
                insights.Add("Health is on an upward trend (3 consecutive improvements)");
            }
            else if (recentScores[2] < recentScores[1] && recentScores[1] < recentScores[0])
            {
                _logger.LogInformation("Trend direction: declining (3 consecutive regressions)");
                insights.Add("Health is declining (3 consecutive regressions)");
            }
        }

        return insights;
    }

    /// <summary>
    /// Extract all dimension scores from a Codebase.
    /// </summary>
    public Dictionary<string, double> GetDimensionScores(Codebase codebase)
    {
        return new Dictionary<string, double>
        {
            ["CodeQuality"] = codebase.Quality.Score,
            ["Testing"] = codebase.Testing.Score,
            ["Dependencies"] = codebase.Dependencies.Score,
            ["Security"] = codebase.Security.Score,
            ["Observability"] = codebase.Observability.Score,
            ["CiCd"] = codebase.Pipeline.Score,
            ["Documentation"] = codebase.Documentation.Score,
            ["DevExperience"] = codebase.DevEx.Score,
            ["Performance"] = codebase.Performance.Score,
            ["Requirements"] = codebase.Requirements.Score,
            ["TeamProcess"] = codebase.Process.Score,
            ["AIQuality"] = codebase.AIQuality.Score,
            ["Architecture"] = codebase.Architecture.Score,
            ["Structure"] = codebase.Structure.Score
        };
    }

    private double GetDimensionScore(Codebase codebase, string dimension) =>
        GetDimensionScores(codebase).TryGetValue(dimension, out var score) ? score : 0;
}
