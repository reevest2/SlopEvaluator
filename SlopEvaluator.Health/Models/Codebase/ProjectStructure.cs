namespace SlopEvaluator.Health.Models;

/// <summary>
/// Solution layout, naming conventions, and project granularity.
/// </summary>
public sealed record ProjectStructure : IScoreable
{
    /// <summary>Total number of projects in the solution.</summary>
    public required int ProjectCount { get; init; }

    /// <summary>Total number of source files across all projects.</summary>
    public required int SourceFiles { get; init; }

    /// <summary>Total lines of code across all source files.</summary>
    public required int TotalLines { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for naming consistency.</summary>
    public required double NamingConsistency { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for folder organization.</summary>
    public required double FolderOrganization { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for solution hygiene.</summary>
    public required double SolutionHygiene { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for appropriate project granularity.</summary>
    public required double ProjectGranularity { get; init; }

    /// <summary>Details for each project in the solution.</summary>
    public required List<ProjectInfo> Projects { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (NamingConsistency, 0.25),
        (FolderOrganization, 0.30),
        (SolutionHygiene, 0.20),
        (ProjectGranularity, 0.25)
    );
}

/// <summary>
/// Metadata for a single project within the solution.
/// </summary>
public sealed record ProjectInfo
{
    /// <summary>Project name.</summary>
    public required string Name { get; init; }

    /// <summary>SDK type (e.g. "Microsoft.NET.Sdk", "Microsoft.NET.Sdk.Web").</summary>
    public required string SdkType { get; init; }

    /// <summary>Output type (e.g. "Library", "Exe").</summary>
    public required string OutputType { get; init; }

    /// <summary>Number of source files in this project.</summary>
    public required int SourceFileCount { get; init; }

    /// <summary>Total lines of code in this project.</summary>
    public required int TotalLines { get; init; }

    /// <summary>Other projects referenced by this project.</summary>
    public required List<string> ProjectReferences { get; init; }

    /// <summary>NuGet packages referenced by this project.</summary>
    public required List<string> PackageReferences { get; init; }
}
