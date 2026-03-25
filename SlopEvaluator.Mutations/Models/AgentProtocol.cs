using SlopEvaluator.Mutations.Analysis;
namespace SlopEvaluator.Mutations.Models;

// ── Harness → Agent messages (written to stdout as JSONL) ─────────

/// <summary>Status update during execution.</summary>
public sealed class ProtocolStatusMessage
{
    public string Type => "status";
    public required string Message { get; init; }
    public string? Phase { get; init; }
}

/// <summary>Sent when the agent loop completes.</summary>
public sealed class ProtocolCompleteMessage
{
    public string Type => "complete";
    public required double FinalScore { get; init; }
    public required int TotalRounds { get; init; }
    public required string Message { get; init; }
}

/// <summary>Sent on fatal errors.</summary>
public sealed class ProtocolErrorMessage
{
    public string Type => "error";
    public required string Message { get; init; }
    public string? Details { get; init; }
}

// ── Agent → Harness commands (read from stdin) ────────────────────

/// <summary>
/// Command from the AI agent, read from stdin in --agent-protocol mode.
/// One JSON object per line.
/// </summary>
public sealed class AgentCommand
{
    /// <summary>"continue", "skip", "focus", "timeout", "stop"</summary>
    public required string Type { get; init; }

    /// <summary>For "skip": mutation ID to exclude from re-run.</summary>
    public string? MutationId { get; init; }

    /// <summary>For "focus": only run mutations in this method.</summary>
    public string? MethodName { get; init; }

    /// <summary>For "timeout": new timeout in seconds.</summary>
    public int? TimeoutSeconds { get; init; }
}
