namespace SlopEvaluator.Health.Models;

/// <summary>
/// Point-in-time capture of all codebase dimensions.
/// </summary>
public sealed record CodebaseSnapshot
{
    /// <summary>Unique identifier for this snapshot.</summary>
    public required Guid Id { get; init; }

    /// <summary>Name of the codebase this snapshot belongs to.</summary>
    public required string CodebaseName { get; init; }

    /// <summary>Date and time the snapshot was taken.</summary>
    public required DateTime TakenAt { get; init; }

    /// <summary>Git commit SHA at the time of the snapshot.</summary>
    public required string GitCommitSha { get; init; }

    /// <summary>Git branch at the time of the snapshot.</summary>
    public required string GitBranch { get; init; }

    /// <summary>Full codebase measurement data.</summary>
    public required Codebase Data { get; init; }

    /// <summary>Delta from the previous snapshot, if available.</summary>
    public SnapshotDelta? DeltaFromPrevious { get; init; }
}

/// <summary>
/// Change between two snapshots — where did we improve or regress?
/// </summary>
public sealed record SnapshotDelta
{
    /// <summary>Identifier of the previous snapshot being compared against.</summary>
    public required Guid PreviousSnapshotId { get; init; }

    /// <summary>Elapsed time between the two snapshots.</summary>
    public required TimeSpan TimeBetween { get; init; }

    /// <summary>Change in overall composite score (positive = improvement).</summary>
    public required double OverallScoreDelta { get; init; }

    /// <summary>Per-dimension score deltas keyed by dimension name.</summary>
    public required Dictionary<string, double> DimensionDeltas { get; init; }

    /// <summary>Dimensions that improved since the previous snapshot.</summary>
    public required List<string> Improvements { get; init; }

    /// <summary>Dimensions that regressed since the previous snapshot.</summary>
    public required List<string> Regressions { get; init; }
}
