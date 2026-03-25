using SlopEvaluator.Mutations.Analysis;
namespace SlopEvaluator.Mutations.Models;

// ═══════════════════════════════════════════════════════════════
//  COVERAGE MODELS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Parsed coverage data from a Cobertura XML report (Coverlet output).
/// </summary>
public sealed class CoverageReport
{
    public required string SourceFile { get; init; }
    public required DateTime GeneratedAt { get; init; }
    public required double LineCoverage { get; init; }
    public required double BranchCoverage { get; init; }
    public required int LinesValid { get; init; }
    public required int LinesCovered { get; init; }
    public required int BranchesValid { get; init; }
    public required int BranchesCovered { get; init; }
    public required List<UncoveredLine> UncoveredLines { get; init; }
    public required List<UncoveredBranch> UncoveredBranches { get; init; }
    public required List<MethodCoverage> Methods { get; init; }
}

public sealed class UncoveredLine
{
    public required int LineNumber { get; init; }
    public required string? SourceSnippet { get; init; }
}

public sealed class UncoveredBranch
{
    public required int LineNumber { get; init; }
    public required int BranchIndex { get; init; }
    public required string Condition { get; init; }
    public required string? SourceSnippet { get; init; }
}

public sealed class MethodCoverage
{
    public required string ClassName { get; init; }
    public required string MethodName { get; init; }
    public required string MethodSignature { get; init; }
    public required double LineCoverage { get; init; }
    public required double BranchCoverage { get; init; }
    public required int LinesValid { get; init; }
    public required int LinesCovered { get; init; }
    public required int BranchesValid { get; init; }
    public required int BranchesCovered { get; init; }
    public required List<int> UncoveredLineNumbers { get; init; }
}

// ═══════════════════════════════════════════════════════════════
//  EDGE CASE MODELS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Analysis of a source file identifying potential edge cases
/// that should have test coverage.
/// </summary>
public sealed class EdgeCaseReport
{
    public required string SourceFile { get; init; }
    public required string? Target { get; init; }
    public required DateTime GeneratedAt { get; init; }
    public required List<EdgeCase> EdgeCases { get; init; }
    public required List<string> ParameterBoundaries { get; init; }
    public required List<string> StateTransitions { get; init; }

    /// <summary>
    /// Summary of edge case categories found and their counts.
    /// </summary>
    public required Dictionary<string, int> CategorySummary { get; init; }
}

public sealed class EdgeCase
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string RiskLevel { get; init; }
    public required int? LineNumber { get; init; }
    public required string? CodeContext { get; init; }

    /// <summary>
    /// A suggested test name following naming conventions.
    /// </summary>
    public required string SuggestedTestName { get; init; }

    /// <summary>
    /// Whether existing tests appear to cover this edge case.
    /// null means we couldn't determine coverage.
    /// </summary>
    public bool? CoveredByExistingTests { get; init; }
}

// ═══════════════════════════════════════════════════════════════
//  COMBINED QUALITY REPORT
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Full quality report combining coverage, edge cases, and mutation testing.
/// This is what gets fed back to Claude for analysis.
/// </summary>
public sealed class QualityReport
{
    public required string SourceFile { get; init; }
    public required string? Target { get; init; }
    public required DateTime RunDate { get; init; }
    public required TimeSpan TotalDuration { get; init; }

    // Coverage
    public CoverageReport? Coverage { get; init; }

    // Edge cases
    public EdgeCaseReport? EdgeCases { get; init; }

    // Mutation testing (from existing MutationReport)
    public MutationReport? MutationResults { get; init; }

    // Computed quality scores
    public required QualityScores Scores { get; init; }

    // Actionable items for Claude or human
    public required List<QualityAction> Actions { get; init; }
}

public sealed class QualityScores
{
    /// <summary>Line coverage % (0-100)</summary>
    public double LineCoverage { get; init; }

    /// <summary>Branch coverage % (0-100)</summary>
    public double BranchCoverage { get; init; }

    /// <summary>Mutation score % (0-100)</summary>
    public double MutationScore { get; init; }

    /// <summary>Edge case coverage % — what fraction of identified edge cases have tests</summary>
    public double EdgeCaseCoverage { get; init; }

    /// <summary>
    /// Composite quality score (0-100).
    /// Weighted: 20% line, 25% branch, 35% mutation, 20% edge case.
    /// </summary>
    public double CompositeScore =>
        (LineCoverage * 0.20) +
        (BranchCoverage * 0.25) +
        (MutationScore * 0.35) +
        (EdgeCaseCoverage * 0.20);
}

public sealed class QualityAction
{
    public required string Priority { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string? File { get; init; }
    public required int? LineNumber { get; init; }
}
