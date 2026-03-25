namespace SlopEvaluator.Health.Models;

/// <summary>
/// PR cycle time, review quality, bus factor, commit hygiene, and incident response.
/// </summary>
public sealed record TeamProcessMetrics : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for PR cycle time health.</summary>
    public required double PrCycleTimeHealth { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for code review quality.</summary>
    public required double ReviewQuality { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for knowledge distribution across the team.</summary>
    public required double KnowledgeDistribution { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for commit message hygiene.</summary>
    public required double CommitHygiene { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for branching strategy health.</summary>
    public required double BranchStrategy { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for incident response readiness.</summary>
    public required double IncidentResponseHealth { get; init; }

    /// <summary>Pull request timing and size metrics.</summary>
    public required PullRequestMetrics PullRequests { get; init; }

    /// <summary>Commit size, convention compliance, and frequency metrics.</summary>
    public required CommitMetrics Commits { get; init; }

    /// <summary>Bus factor, contributor distribution, and file ownership.</summary>
    public required KnowledgeMetrics Knowledge { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (PrCycleTimeHealth, 0.20),
        (ReviewQuality, 0.20),
        (KnowledgeDistribution, 0.15),
        (CommitHygiene, 0.15),
        (BranchStrategy, 0.10),
        (IncidentResponseHealth, 0.20)
    );
}

/// <summary>
/// Pull request timing, size, and review engagement metrics.
/// </summary>
public sealed record PullRequestMetrics
{
    /// <summary>Median time from PR creation to first review.</summary>
    public required TimeSpan MedianTimeToFirstReview { get; init; }

    /// <summary>Median time from PR creation to merge.</summary>
    public required TimeSpan MedianTimeToMerge { get; init; }

    /// <summary>Average number of review comments per PR.</summary>
    public required double AverageReviewComments { get; init; }

    /// <summary>Rate of PRs approved without any comments (0.0 to 1.0).</summary>
    public required double ApprovalWithoutCommentsRate { get; init; }

    /// <summary>Number of pull requests merged per week.</summary>
    public required int PrsPerWeek { get; init; }

    /// <summary>Average lines changed per PR.</summary>
    public required double AveragePrSizeLines { get; init; }

    /// <summary>Rate of PRs exceeding the size threshold (0.0 to 1.0).</summary>
    public required double LargePrRate { get; init; }

    /// <summary>Number of stale (long-open) pull requests.</summary>
    public required int StalePrCount { get; init; }
}

/// <summary>
/// Commit frequency, convention compliance, and hygiene metrics.
/// </summary>
public sealed record CommitMetrics
{
    /// <summary>Average number of lines changed per commit.</summary>
    public required double AverageCommitSize { get; init; }

    /// <summary>Rate of commits following conventional commit format (0.0 to 1.0).</summary>
    public required double ConventionalCommitCompliance { get; init; }

    /// <summary>Average number of commits per day.</summary>
    public required double CommitsPerDayAverage { get; init; }

    /// <summary>Rate of merge commits vs. rebase/squash (0.0 to 1.0).</summary>
    public required double MergeCommitRate { get; init; }

    /// <summary>Number of force pushes in the measured period.</summary>
    public required int ForcePushCount { get; init; }
}

/// <summary>
/// Knowledge distribution, bus factor, and file ownership metrics.
/// </summary>
public sealed record KnowledgeMetrics
{
    /// <summary>Minimum number of contributors who must leave before knowledge is lost.</summary>
    public required int BusFactor { get; init; }

    /// <summary>Percentage of commits from the top contributor (0.0 to 1.0).</summary>
    public required double TopContributorConcentration { get; init; }

    /// <summary>Number of contributors active in the last 30 days.</summary>
    public required int ActiveContributors30d { get; init; }

    /// <summary>Total commits keyed by author name.</summary>
    public required Dictionary<string, int> CommitsByAuthor { get; init; }

    /// <summary>Primary owners keyed by file path.</summary>
    public required Dictionary<string, List<string>> FileOwnership { get; init; }

    /// <summary>Number of files with only a single contributor.</summary>
    public required int SingleOwnerFileCount { get; init; }

    /// <summary>Percentage of files with only a single contributor (0.0 to 1.0).</summary>
    public required double SingleOwnerFilePercent { get; init; }
}
