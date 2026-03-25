using System.Text.Json.Serialization;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Models;

// ═══════════════════════════════════════════════════════════════
//  TOKEN COUNTER (pluggable for future Claude API integration)
// ═══════════════════════════════════════════════════════════════

public interface ITokenCounter
{
    void RecordUsage(string operationName, int inputTokens, int outputTokens, string? model = null);
    TokenUsageSummary GetSummary();
}

public sealed class NoOpTokenCounter : ITokenCounter
{
    public void RecordUsage(string operationName, int inputTokens, int outputTokens, string? model = null) { }
    public TokenUsageSummary GetSummary() => new() { TotalInputTokens = 0, TotalOutputTokens = 0, Entries = [] };
}

public sealed class TokenUsageEntry
{
    public required string OperationName { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public string? Model { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed class TokenUsageSummary
{
    public required int TotalInputTokens { get; init; }
    public required int TotalOutputTokens { get; init; }
    public required List<TokenUsageEntry> Entries { get; init; }
}

// ═══════════════════════════════════════════════════════════════
//  METRIC EVENTS (each becomes one JSONL line)
// ═══════════════════════════════════════════════════════════════

[JsonDerivedType(typeof(SessionStartEvent), "sessionStart")]
[JsonDerivedType(typeof(PhaseTimingEvent), "phaseTiming")]
[JsonDerivedType(typeof(ProcessExecutionEvent), "processExecution")]
[JsonDerivedType(typeof(MutationMetricEvent), "mutation")]
[JsonDerivedType(typeof(AggregateStatsEvent), "aggregateStats")]
public abstract class MetricEvent
{
    public required string EventType { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed class SessionStartEvent : MetricEvent
{
    public required string MachineName { get; init; }
    public required string OsPlatform { get; init; }
    public required string OsVersion { get; init; }
    public required string DotNetVersion { get; init; }
    public required string Command { get; init; }
    public required string SourceFile { get; init; }
    public string? Target { get; init; }
    public int MutationCount { get; init; }
}

public sealed class PhaseTimingEvent : MetricEvent
{
    public required string Phase { get; init; }
    public required double DurationMs { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
}

public sealed class ProcessExecutionEvent : MetricEvent
{
    public required string Operation { get; init; }
    public required string MutationId { get; init; }
    public required int ExitCode { get; init; }
    public required double DurationMs { get; init; }
    public required long StdoutBytes { get; init; }
    public required long StderrBytes { get; init; }
    public long? PeakWorkingSetBytes { get; init; }
}

public sealed class MutationMetricEvent : MetricEvent
{
    public required string MutationId { get; init; }
    public required string Strategy { get; init; }
    public required string RiskLevel { get; init; }
    public required string Outcome { get; init; }
    public required double TotalDurationMs { get; init; }
    public double? ApplyDurationMs { get; init; }
    public double? BuildDurationMs { get; init; }
    public double? TestDurationMs { get; init; }
    public bool ApplySuccess { get; init; }
    public int TestsRun { get; init; }
    public int TestsFailed { get; init; }
}

public sealed class AggregateStatsEvent : MetricEvent
{
    public required int TotalMutations { get; init; }
    public required int Killed { get; init; }
    public required int Survived { get; init; }
    public required int CompileErrors { get; init; }
    public required int Timeouts { get; init; }
    public required int Errors { get; init; }
    public required double MutationScore { get; init; }
    public required double TotalDurationSeconds { get; init; }
    public required double MutationsPerMinute { get; init; }
    public double? LineCoverage { get; init; }
    public double? BranchCoverage { get; init; }
    public int? EdgeCasesFound { get; init; }
    public int? EdgeCasesUncovered { get; init; }
    public TokenUsageSummary? TokenUsage { get; init; }
}
