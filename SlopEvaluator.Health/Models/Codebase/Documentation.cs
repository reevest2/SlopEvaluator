namespace SlopEvaluator.Health.Models;

/// <summary>
/// README completeness, API doc coverage, ADRs, freshness, and onboarding quality.
/// </summary>
public sealed record Documentation : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for README completeness.</summary>
    public required double ReadmeCompleteness { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for public API documentation coverage.</summary>
    public required double ApiDocCoverage { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for Architecture Decision Record presence.</summary>
    public required double AdrPresence { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for documentation freshness.</summary>
    public required double DocFreshness { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for inline comment quality.</summary>
    public required double InlineCommentQuality { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for onboarding documentation.</summary>
    public required double OnboardingDocumentation { get; init; }

    /// <summary>Inventory of documentation artifacts in the repository.</summary>
    public required DocInventory Inventory { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (ReadmeCompleteness, 0.20),
        (ApiDocCoverage, 0.25),
        (AdrPresence, 0.10),
        (DocFreshness, 0.15),
        (InlineCommentQuality, 0.15),
        (OnboardingDocumentation, 0.15)
    );
}

/// <summary>
/// Inventory of documentation files and coverage statistics.
/// </summary>
public sealed record DocInventory
{
    /// <summary>Whether a README file exists.</summary>
    public required bool ReadmeExists { get; init; }

    /// <summary>Whether a CHANGELOG file exists.</summary>
    public required bool ChangelogExists { get; init; }

    /// <summary>Whether a CONTRIBUTING guide exists.</summary>
    public required bool ContributingGuideExists { get; init; }

    /// <summary>Whether a CLAUDE.md file exists for AI context.</summary>
    public required bool ClaudeMdExists { get; init; }

    /// <summary>Number of Architecture Decision Records found.</summary>
    public required int AdrCount { get; init; }

    /// <summary>Total number of public members across all projects.</summary>
    public required int PublicMembersTotal { get; init; }

    /// <summary>Number of public members that have XML documentation.</summary>
    public required int PublicMembersDocumented { get; init; }

    /// <summary>Last modification date of the README, if it exists.</summary>
    public DateTime? ReadmeLastModified { get; init; }

    /// <summary>Date of the most recent ADR, if any exist.</summary>
    public DateTime? MostRecentAdr { get; init; }

    /// <summary>Documentation coverage percentage keyed by project name.</summary>
    public required Dictionary<string, double> DocCoverageByProject { get; init; }
}
