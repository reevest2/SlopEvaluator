using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Collects operational metrics during a harness run and flushes them
/// as a JSONL file for later analysis.
/// </summary>
public sealed class MetricsCollector
{
    private readonly List<MetricEvent> _events = [];
    private readonly ITokenCounter _tokenCounter;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MetricsCollector(ITokenCounter? tokenCounter = null)
    {
        _tokenCounter = tokenCounter ?? new NoOpTokenCounter();
    }

    public ITokenCounter TokenCounter => _tokenCounter;

    public void Record(MetricEvent evt) => _events.Add(evt);

    public void RecordSessionStart(string command, string sourceFile, string? target, int mutationCount)
    {
        Record(new SessionStartEvent
        {
            EventType = "sessionStart",
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            OsPlatform = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            Command = command,
            SourceFile = sourceFile,
            Target = target,
            MutationCount = mutationCount
        });
    }

    public void RecordPhase(string phase, double durationMs, bool success, string? error = null)
    {
        Record(new PhaseTimingEvent
        {
            EventType = "phaseTiming",
            Timestamp = DateTime.UtcNow,
            Phase = phase,
            DurationMs = durationMs,
            Success = success,
            Error = error
        });
    }

    public void RecordProcessExecution(
        string operation, string mutationId, int exitCode,
        double durationMs, long stdoutBytes, long stderrBytes,
        long? peakWorkingSetBytes = null)
    {
        Record(new ProcessExecutionEvent
        {
            EventType = "processExecution",
            Timestamp = DateTime.UtcNow,
            Operation = operation,
            MutationId = mutationId,
            ExitCode = exitCode,
            DurationMs = durationMs,
            StdoutBytes = stdoutBytes,
            StderrBytes = stderrBytes,
            PeakWorkingSetBytes = peakWorkingSetBytes
        });
    }

    public void RecordMutation(
        string mutationId, string strategy, string riskLevel, string outcome,
        double totalMs, double? applyMs, double? buildMs, double? testMs,
        bool applySuccess, int testsRun, int testsFailed)
    {
        Record(new MutationMetricEvent
        {
            EventType = "mutation",
            Timestamp = DateTime.UtcNow,
            MutationId = mutationId,
            Strategy = strategy,
            RiskLevel = riskLevel,
            Outcome = outcome,
            TotalDurationMs = totalMs,
            ApplyDurationMs = applyMs,
            BuildDurationMs = buildMs,
            TestDurationMs = testMs,
            ApplySuccess = applySuccess,
            TestsRun = testsRun,
            TestsFailed = testsFailed
        });
    }

    public void RecordAggregateStats(
        MutationReport? mutationReport,
        CoverageReport? coverage,
        EdgeCaseReport? edgeCases,
        double totalDurationSeconds)
    {
        int total = mutationReport?.Results.Count ?? 0;
        int killed = mutationReport?.Killed ?? 0;
        int survived = mutationReport?.Survived ?? 0;
        double mutPerMin = totalDurationSeconds > 0 ? total / (totalDurationSeconds / 60.0) : 0;

        int? ecFound = edgeCases?.EdgeCases.Count;
        int? ecUncovered = edgeCases?.EdgeCases.Count(e => e.CoveredByExistingTests != true);

        Record(new AggregateStatsEvent
        {
            EventType = "aggregateStats",
            Timestamp = DateTime.UtcNow,
            TotalMutations = total,
            Killed = killed,
            Survived = survived,
            CompileErrors = mutationReport?.CompileErrors ?? 0,
            Timeouts = mutationReport?.Timeouts ?? 0,
            Errors = mutationReport?.Results.Count(r => r.Outcome == MutationOutcome.Error) ?? 0,
            MutationScore = mutationReport?.MutationScore ?? 0,
            TotalDurationSeconds = totalDurationSeconds,
            MutationsPerMinute = mutPerMin,
            LineCoverage = coverage?.LineCoverage,
            BranchCoverage = coverage?.BranchCoverage,
            EdgeCasesFound = ecFound,
            EdgeCasesUncovered = ecUncovered,
            TokenUsage = _tokenCounter.GetSummary() is { TotalInputTokens: > 0 } s ? s : null
        });
    }

    public async Task FlushAsync(string path)
    {
        if (_events.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var evt in _events)
        {
            sb.AppendLine(JsonSerializer.Serialize<MetricEvent>(evt, JsonOptions));
        }

        await File.WriteAllTextAsync(path, sb.ToString());
    }
}
