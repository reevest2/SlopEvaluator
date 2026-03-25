using System.Text.Json;
using System.Text.Json.Serialization;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Tracks mutation score history and computes trends across runs.
/// </summary>
public static class TrendAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Appends the current run to a history file.
    /// </summary>
    public static async Task RecordRunAsync(MutationReport report, string historyPath)
    {
        await RecordRunAsync(report, historyPath, tokenUsage: null, round: null, sessionId: null);
    }

    /// <summary>
    /// Appends the current run to a history file with optional token tracking data.
    /// </summary>
    public static async Task RecordRunAsync(
        MutationReport report, string historyPath,
        TokenUsageSummary? tokenUsage,
        int? round,
        string? sessionId)
    {
        var entry = new TrendEntry
        {
            Timestamp = report.RunDate,
            Target = report.Target,
            SourceFile = report.SourceFile,
            MutationScore = Math.Round(report.MutationScore, 1),
            Killed = report.Killed,
            Survived = report.Survived,
            CompileErrors = report.CompileErrors,
            Total = report.Results.Count,
            Duration = report.TotalDuration,
            SurvivorIds = report.Results
                .Where(r => r.Outcome == MutationOutcome.Survived)
                .Select(r => r.Id)
                .ToList(),
            StrategyResults = report.Results
                .GroupBy(r => r.Strategy)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(
                    g => g.Key,
                    g => new StrategyOutcome
                    {
                        Killed = g.Count(r => r.Outcome == MutationOutcome.Killed),
                        Survived = g.Count(r => r.Outcome == MutationOutcome.Survived),
                        CompileError = g.Count(r => r.Outcome == MutationOutcome.CompileError)
                    }),
            InputTokens = tokenUsage?.TotalInputTokens,
            OutputTokens = tokenUsage?.TotalOutputTokens,
            TokenCost = tokenUsage is not null
                ? Models.ModelPricing.ComputeCost(tokenUsage.TotalInputTokens, tokenUsage.TotalOutputTokens)
                : null,
            Round = round,
            SessionId = sessionId
        };

        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await File.AppendAllTextAsync(historyPath, json + Environment.NewLine);
    }

    /// <summary>
    /// Reads history and returns trend data.
    /// </summary>
    public static TrendReport Analyze(string historyPath, string? target = null)
    {
        if (!File.Exists(historyPath))
            return new TrendReport { Entries = [], Summary = "No history found." };

        var lines = File.ReadAllLines(historyPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var entries = new List<TrendEntry>();
        foreach (var line in lines)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<TrendEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    if (target is null || entry.Target == target)
                        entries.Add(entry);
                }
            }
            catch { /* skip malformed lines */ }
        }

        if (entries.Count == 0)
            return new TrendReport { Entries = [], Summary = "No matching entries found." };

        var latest = entries.Last();
        var previous = entries.Count > 1 ? entries[^2] : null;
        var delta = previous is not null ? latest.MutationScore - previous.MutationScore : 0;
        var deltaStr = delta > 0 ? $"\u2191{delta:F1}%" : delta < 0 ? $"\u2193{Math.Abs(delta):F1}%" : "unchanged";
        var best = entries.Max(e => e.MutationScore);
        var worst = entries.Min(e => e.MutationScore);

        return new TrendReport
        {
            Entries = entries,
            Summary = $"Score: {latest.MutationScore}% ({deltaStr} from last run). " +
                      $"Range: {worst}% \u2013 {best}% across {entries.Count} runs."
        };
    }

    /// <summary>
    /// Compares two reports side-by-side.
    /// </summary>
    public static ComparisonResult Compare(MutationReport oldReport, MutationReport newReport)
    {
        var oldSurvivorIds = oldReport.Results
            .Where(r => r.Outcome == MutationOutcome.Survived)
            .Select(r => r.Id).ToHashSet();

        var newSurvivorIds = newReport.Results
            .Where(r => r.Outcome == MutationOutcome.Survived)
            .Select(r => r.Id).ToHashSet();

        return new ComparisonResult
        {
            OldScore = oldReport.MutationScore,
            NewScore = newReport.MutationScore,
            ScoreDelta = newReport.MutationScore - oldReport.MutationScore,
            NewlyKilled = oldSurvivorIds.Except(newSurvivorIds).ToList(),
            NewSurvivors = newSurvivorIds.Except(oldSurvivorIds).ToList(),
            StillSurviving = oldSurvivorIds.Intersect(newSurvivorIds).ToList()
        };
    }
}

public sealed class TrendEntry
{
    public DateTime Timestamp { get; init; }
    public string Target { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public double MutationScore { get; init; }
    public int Killed { get; init; }
    public int Survived { get; init; }
    public int CompileErrors { get; init; }
    public int Total { get; init; }
    public TimeSpan Duration { get; init; }
    public List<string> SurvivorIds { get; init; } = [];

    // Token efficiency tracking (nullable for backward compat with existing history)
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public double? TokenCost { get; init; }
    public int? Round { get; init; }
    public string? SessionId { get; init; }

    /// <summary>Per-strategy outcome counts for smart mutation selection.</summary>
    public Dictionary<string, StrategyOutcome>? StrategyResults { get; init; }
}

/// <summary>
/// Outcome counts for a single mutation strategy in a run.
/// </summary>
public sealed class StrategyOutcome
{
    public int Killed { get; init; }
    public int Survived { get; init; }
    public int CompileError { get; init; }
}

public sealed class TrendReport
{
    public List<TrendEntry> Entries { get; init; } = [];
    public string Summary { get; init; } = "";
}

public sealed class ComparisonResult
{
    public double OldScore { get; init; }
    public double NewScore { get; init; }
    public double ScoreDelta { get; init; }
    public List<string> NewlyKilled { get; init; } = [];
    public List<string> NewSurvivors { get; init; } = [];
    public List<string> StillSurviving { get; init; } = [];
}
