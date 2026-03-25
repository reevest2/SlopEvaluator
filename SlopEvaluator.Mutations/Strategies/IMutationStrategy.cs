using Microsoft.CodeAnalysis;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Strategies;

/// <summary>
/// A pluggable mutation strategy that can generate candidates from
/// a Roslyn syntax tree and apply structural mutations that go beyond
/// text find/replace.
/// </summary>
public interface IMutationStrategy
{
    /// <summary>Strategy identifier matching MutationSpec.Strategy (e.g., "remove-statement").</summary>
    string Name { get; }

    /// <summary>
    /// Scan a syntax tree and return mutation candidates this strategy can produce.
    /// </summary>
    IReadOnlyList<MutationCandidate> FindCandidates(SyntaxTree tree, SyntaxNode root, string sourceFile);

    /// <summary>
    /// Apply a structural mutation to the source. Returns the new source text,
    /// or null if this strategy cannot handle the given spec (fall through to text-based).
    /// </summary>
    string? ApplyStructural(SyntaxTree tree, SyntaxNode root, MutationSpec spec);
}
