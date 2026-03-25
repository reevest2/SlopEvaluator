namespace SlopEvaluator.Health.Models;

/// <summary>
/// NuGet package staleness, vulnerabilities, licenses, and transitive dependency health.
/// </summary>
public sealed record DependencyHealth : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for package freshness.</summary>
    public required double Freshness { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for freedom from known vulnerabilities.</summary>
    public required double VulnerabilityFreedom { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for license compliance.</summary>
    public required double LicenseCompliance { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for transitive dependency cleanliness.</summary>
    public required double TransitiveCleanliness { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for appropriate package count.</summary>
    public required double PackageCountScore { get; init; }

    /// <summary>All NuGet packages referenced by the solution.</summary>
    public required List<PackageInfo> Packages { get; init; }

    /// <summary>Known security vulnerabilities in referenced packages.</summary>
    public required List<VulnerabilityInfo> Vulnerabilities { get; init; }

    /// <summary>Packages marked as deprecated by their maintainers.</summary>
    public required List<DeprecatedPackage> Deprecated { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (Freshness, 0.20),
        (VulnerabilityFreedom, 0.35),
        (LicenseCompliance, 0.20),
        (TransitiveCleanliness, 0.15),
        (PackageCountScore, 0.10)
    );
}

/// <summary>
/// Information about a single NuGet package reference.
/// </summary>
public sealed record PackageInfo
{
    /// <summary>NuGet package identifier.</summary>
    public required string PackageId { get; init; }

    /// <summary>Currently referenced version.</summary>
    public required string CurrentVersion { get; init; }

    /// <summary>Latest available version, if known.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>Number of major versions behind the latest release.</summary>
    public required int MajorVersionsBehind { get; init; }

    /// <summary>Days since the package was last updated on NuGet.</summary>
    public required int DaysSinceLastUpdate { get; init; }

    /// <summary>SPDX license identifier for the package.</summary>
    public required string License { get; init; }

    /// <summary>Whether the package is marked as deprecated.</summary>
    public required bool IsDeprecated { get; init; }

    /// <summary>Whether this is a direct (not transitive) reference.</summary>
    public required bool IsDirect { get; init; }

    /// <summary>Project that references this package.</summary>
    public required string Project { get; init; }
}

/// <summary>
/// A known security vulnerability in a referenced package.
/// </summary>
public sealed record VulnerabilityInfo
{
    /// <summary>NuGet package identifier.</summary>
    public required string PackageId { get; init; }

    /// <summary>Advisory identifier (e.g. CVE or GHSA).</summary>
    public required string AdvisoryId { get; init; }

    /// <summary>Severity level (e.g. "Critical", "High", "Medium", "Low").</summary>
    public required string Severity { get; init; }

    /// <summary>Human-readable description of the vulnerability.</summary>
    public required string Description { get; init; }

    /// <summary>Package version that resolves the vulnerability, if available.</summary>
    public string? FixedInVersion { get; init; }
}

/// <summary>
/// A package that has been deprecated by its maintainer.
/// </summary>
public sealed record DeprecatedPackage
{
    /// <summary>NuGet package identifier.</summary>
    public required string PackageId { get; init; }

    /// <summary>Reason for deprecation.</summary>
    public required string Reason { get; init; }

    /// <summary>Suggested replacement package, if any.</summary>
    public string? AlternativePackage { get; init; }
}
