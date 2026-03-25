using SlopEvaluator.Mutations.Analysis;
namespace SlopEvaluator.Mutations.Models;

/// <summary>
/// A mutation candidate discovered by scanning a syntax tree.
/// Used by both the RoslynMutationGenerator walker and IMutationStrategy implementations.
/// </summary>
public sealed record MutationCandidate
{
    public required string Strategy { get; init; }
    public required string Description { get; init; }
    public required string OriginalCode { get; init; }
    public required string MutatedCode { get; init; }
    public required string RiskLevel { get; init; }
    public required int LineNumber { get; init; }

    /// <summary>
    /// Optional: fully-qualified method name containing this candidate.
    /// Required for structural mutations (remove-statement, swap-statements, etc.).
    /// </summary>
    public string? TargetMethod { get; init; }

    /// <summary>
    /// Optional: 0-based index of the target node within the method.
    /// </summary>
    public int? NodeIndex { get; init; }

    /// <summary>
    /// Optional: strategy-specific parameters.
    /// </summary>
    public Dictionary<string, string>? Parameters { get; init; }
}
