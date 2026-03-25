using System.Text.Json;
using System.Text.Json.Serialization;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;

namespace SlopEvaluator.Mutations.Analysis;

/// <summary>
/// Analyzes token efficiency of mutation testing runs.
/// Fits a diminishing-returns curve and recommends optimal stopping points.
/// </summary>
public static class TokenEfficiencyAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Reads history and metrics, computes per-entry efficiency, fits curve, generates recommendations.
    /// </summary>
    public static TokenEfficiencyReport Analyze(string historyPath, TokenEfficiencyOptions? options = null)
    {
        options ??= new TokenEfficiencyOptions();

        var entries = LoadEntries(historyPath, options);

        var curve = entries.Count >= 3
            ? FitDiminishingReturns(entries, options.StopThreshold)
            : null;

        var summary = ComputeSummary(entries);
        var recommendations = GenerateRecommendations(curve, entries, options);

        return new TokenEfficiencyReport
        {
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            Target = options.Target,
            Entries = entries,
            Curve = curve,
            Summary = summary,
            Recommendations = recommendations
        };
    }

    /// <summary>
    /// Loads trend entries and converts to efficiency entries, grouping by session.
    /// </summary>
    internal static List<EfficiencyEntry> LoadEntries(string historyPath, TokenEfficiencyOptions options)
    {
        if (!File.Exists(historyPath))
            return [];

        var lines = File.ReadAllLines(historyPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var trendEntries = new List<TrendEntry>();
        foreach (var line in lines)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<TrendEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    if (options.Target is null || entry.Target == options.Target)
                        trendEntries.Add(entry);
                }
            }
            catch { /* skip malformed lines */ }
        }

        // Group by session (if available), otherwise treat each entry as its own session
        var sessions = trendEntries
            .GroupBy(e => e.SessionId ?? e.Timestamp.ToString("O"))
            .ToList();

        var result = new List<EfficiencyEntry>();

        foreach (var session in sessions)
        {
            var ordered = session.OrderBy(e => e.Round ?? 0).ThenBy(e => e.Timestamp).ToList();
            double previousScore = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                var te = ordered[i];
                var scoreDelta = te.MutationScore - previousScore;

                // Estimate survivors killed from score delta and total mutations
                var survivorsKilled = i > 0 && ordered[i - 1].Survived > te.Survived
                    ? ordered[i - 1].Survived - te.Survived
                    : (int)Math.Max(0, Math.Round(scoreDelta / 100.0 * te.Total));

                var value = ComputeValue(scoreDelta, survivorsKilled);
                var cost = te.InputTokens.HasValue && te.OutputTokens.HasValue
                    ? ModelPricing.ComputeCost(te.InputTokens.Value, te.OutputTokens.Value,
                        options.InputPricePerMTok, options.OutputPricePerMTok)
                    : te.TokenCost ?? 0;

                result.Add(new EfficiencyEntry
                {
                    Timestamp = te.Timestamp,
                    Target = te.Target,
                    Round = te.Round ?? (i + 1),
                    SessionId = te.SessionId,
                    InputTokens = te.InputTokens ?? 0,
                    OutputTokens = te.OutputTokens ?? 0,
                    TokenCost = cost,
                    MutationScoreBefore = previousScore,
                    MutationScoreAfter = te.MutationScore,
                    ScoreDelta = scoreDelta,
                    SurvivorsKilled = survivorsKilled,
                    Value = value,
                    Efficiency = cost > 0 ? value / cost : 0
                });

                previousScore = te.MutationScore;
            }
        }

        return result;
    }

    /// <summary>
    /// Computes value from score improvement and bugs found.
    /// </summary>
    /// <summary>
    /// Computes value from score improvement and bugs found, weighted by risk level.
    /// </summary>
    /// <param name="scoreDelta">Change in mutation score (percentage points).</param>
    /// <param name="survivorsKilled">Number of surviving mutants killed in this round.</param>
    /// <param name="riskLevel">Risk level of the killed mutants ("high", "medium", or "low").</param>
    /// <returns>A composite value score combining score improvement and bug-finding impact.</returns>
    public static double ComputeValue(double scoreDelta, int survivorsKilled, string riskLevel = "medium")
    {
        var riskWeight = riskLevel switch
        {
            "high" => 3.0,
            "medium" => 2.0,
            "low" => 1.0,
            _ => 2.0
        };
        return scoreDelta * 10.0 + survivorsKilled * riskWeight;
    }

    /// <summary>
    /// Fits V(r) = A * e^(-lambda * r) to the marginal value data.
    /// Uses OLS on log-transformed data: ln(V) = ln(A) - lambda * r
    /// </summary>
    public static DiminishingReturnsCurve FitExponentialDecay(
        List<(int round, double marginalValue)> data, double stopThreshold = 0.1)
    {
        if (data.Count == 0)
            return DefaultCurve(0, stopThreshold);

        // Filter to positive values (can't log-transform zero/negative)
        var valid = data.Where(d => d.marginalValue > 0).ToList();
        if (valid.Count < 2)
            return DefaultCurve(data.Count, stopThreshold);

        int n = valid.Count;
        var x = valid.Select(d => (double)d.round).ToArray();
        var y = valid.Select(d => Math.Log(d.marginalValue)).ToArray();

        // OLS: slope = (n*Σxy - Σx*Σy) / (n*Σx² - (Σx)²)
        double sumX = x.Sum();
        double sumY = y.Sum();
        double sumXY = x.Zip(y, (xi, yi) => xi * yi).Sum();
        double sumX2 = x.Sum(xi => xi * xi);

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-10)
            return DefaultCurve(data.Count, stopThreshold);

        double slope = (n * sumXY - sumX * sumY) / denom;
        double intercept = (sumY - slope * sumX) / n;

        double A = Math.Exp(intercept);
        double lambda = -slope; // slope is negative for decay

        // R² on log-transformed data
        double yMean = sumY / n;
        double ssTot = y.Sum(yi => (yi - yMean) * (yi - yMean));
        double ssRes = 0;
        for (int i = 0; i < n; i++)
        {
            double predicted = intercept + slope * x[i];
            ssRes += (y[i] - predicted) * (y[i] - predicted);
        }
        double rSquared = ssTot > 0 ? 1.0 - ssRes / ssTot : 0;

        // Optimal stop: A * e^(-lambda * r) < threshold
        // r > ln(A / threshold) / lambda
        int optimalStop = lambda > 0 && A > stopThreshold
            ? (int)Math.Ceiling(Math.Log(A / stopThreshold) / lambda)
            : 10; // default if decay is zero or negative

        return new DiminishingReturnsCurve
        {
            A = Math.Round(A, 4),
            Lambda = Math.Round(lambda, 4),
            RSquared = Math.Round(Math.Max(0, Math.Min(1, rSquared)), 4),
            OptimalStopRound = optimalStop,
            DataPoints = data.Count
        };
    }

    /// <summary>
    /// Fits the diminishing returns curve from efficiency entries grouped by round.
    /// </summary>
    internal static DiminishingReturnsCurve FitDiminishingReturns(
        List<EfficiencyEntry> entries, double stopThreshold)
    {
        // Aggregate value by round number
        var byRound = entries
            .GroupBy(e => e.Round)
            .Select(g => (round: g.Key, marginalValue: g.Average(e => e.Value)))
            .OrderBy(x => x.round)
            .ToList();

        return FitExponentialDecay(byRound, stopThreshold);
    }

    internal static EfficiencySummary ComputeSummary(List<EfficiencyEntry> entries)
    {
        if (entries.Count == 0)
        {
            return new EfficiencySummary
            {
                TotalTokensSpent = 0,
                TotalCostEstimate = 0,
                TotalValueGenerated = 0,
                OverallEfficiency = 0,
                BestRound = 0,
                WorstRound = 0,
                AverageEfficiency = 0,
                TotalRounds = 0
            };
        }

        var totalTokens = entries.Sum(e => (long)e.InputTokens + e.OutputTokens);
        var totalCost = entries.Sum(e => e.TokenCost);
        var totalValue = entries.Sum(e => e.Value);
        var entriesWithCost = entries.Where(e => e.TokenCost > 0).ToList();

        var bestEntry = entriesWithCost.Count > 0
            ? entriesWithCost.MaxBy(e => e.Efficiency)
            : entries.First();
        var worstEntry = entriesWithCost.Count > 0
            ? entriesWithCost.MinBy(e => e.Efficiency)
            : entries.Last();

        return new EfficiencySummary
        {
            TotalTokensSpent = totalTokens,
            TotalCostEstimate = Math.Round(totalCost, 6),
            TotalValueGenerated = Math.Round(totalValue, 2),
            OverallEfficiency = totalCost > 0 ? Math.Round(totalValue / totalCost, 2) : 0,
            BestRound = bestEntry?.Round ?? 0,
            WorstRound = worstEntry?.Round ?? 0,
            AverageEfficiency = entriesWithCost.Count > 0
                ? Math.Round(entriesWithCost.Average(e => e.Efficiency), 2)
                : 0,
            TotalRounds = entries.Count
        };
    }

    internal static List<EfficiencyRecommendation> GenerateRecommendations(
        DiminishingReturnsCurve? curve, List<EfficiencyEntry> entries, TokenEfficiencyOptions options)
    {
        var recs = new List<EfficiencyRecommendation>();

        if (entries.Count < 3)
        {
            recs.Add(new EfficiencyRecommendation
            {
                Type = "more-data",
                Description = $"Only {entries.Count} data point(s) available. Run at least 3 agent rounds to enable curve fitting and reliable recommendations.",
                Confidence = 0.3
            });
            return recs;
        }

        // Check if no entries have token data
        if (entries.All(e => e.InputTokens == 0 && e.OutputTokens == 0))
        {
            recs.Add(new EfficiencyRecommendation
            {
                Type = "more-data",
                Description = "No token usage data found. Use --agent mode with a token-tracking AI integration to collect cost data.",
                Confidence = 0.5
            });
        }

        if (curve is not null)
        {
            // High R² means the exponential decay model fits well
            if (curve.RSquared >= 0.5)
            {
                recs.Add(new EfficiencyRecommendation
                {
                    Type = "stop",
                    Description = $"Diminishing returns detected (R²={curve.RSquared:F2}). " +
                                  $"Optimal stopping point: round {curve.OptimalStopRound}. " +
                                  $"Beyond this, marginal value per token drops below threshold.",
                    Confidence = Math.Min(0.95, curve.RSquared)
                });
            }

            // Fast decay suggests too many mutations per round
            if (curve.Lambda > 1.0)
            {
                recs.Add(new EfficiencyRecommendation
                {
                    Type = "batch-size",
                    Description = $"Rapid decay rate (λ={curve.Lambda:F2}) suggests reducing --max-mutations. " +
                                  "Try fewer mutations per round with more targeted strategies.",
                    Confidence = 0.7
                });
            }

            // Slow decay means runs are still productive
            if (curve.Lambda < 0.3 && curve.RSquared >= 0.5)
            {
                recs.Add(new EfficiencyRecommendation
                {
                    Type = "continue",
                    Description = $"Slow decay rate (λ={curve.Lambda:F2}) — additional rounds are still cost-effective. " +
                                  "Consider increasing --max-rounds.",
                    Confidence = Math.Min(0.9, curve.RSquared)
                });
            }
        }

        // Check last entry's efficiency
        var lastEntry = entries.LastOrDefault();
        if (lastEntry is not null && lastEntry.Efficiency > 0 && lastEntry.Efficiency < options.StopThreshold)
        {
            recs.Add(new EfficiencyRecommendation
            {
                Type = "stop",
                Description = $"Last round efficiency ({lastEntry.Efficiency:F2}) is below threshold ({options.StopThreshold}). " +
                              "Further rounds are unlikely to be cost-effective.",
                Confidence = 0.8
            });
        }

        // Score-based recommendation
        if (lastEntry is not null && lastEntry.MutationScoreAfter >= 95)
        {
            recs.Add(new EfficiencyRecommendation
            {
                Type = "schedule",
                Description = $"Mutation score is {lastEntry.MutationScoreAfter:F1}%. " +
                              "Run mutation testing only on significant code changes or before releases.",
                Confidence = 0.85
            });
        }

        if (recs.Count == 0)
        {
            recs.Add(new EfficiencyRecommendation
            {
                Type = "continue",
                Description = "Insufficient signal to make a strong recommendation. Continue collecting data.",
                Confidence = 0.4
            });
        }

        return recs;
    }

    private static DiminishingReturnsCurve DefaultCurve(int dataPoints, double stopThreshold) => new()
    {
        A = 0,
        Lambda = 0,
        RSquared = 0,
        OptimalStopRound = 5,
        DataPoints = dataPoints
    };
}
