namespace SlopEvaluator.Health.Models;

/// <summary>
/// Secrets hygiene, auth patterns, OWASP Top 10, input validation, and cryptography practices.
/// </summary>
public sealed record SecurityPosture : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for secrets management hygiene.</summary>
    public required double SecretHygiene { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for authentication/authorization patterns.</summary>
    public required double AuthPatterns { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for input validation coverage.</summary>
    public required double InputValidation { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for cryptography best practices.</summary>
    public required double CryptographyPractice { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for dependency-level security.</summary>
    public required double DependencySecurity { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for HTTP security headers and TLS.</summary>
    public required double HttpSecurity { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for data protection practices.</summary>
    public required double DataProtection { get; init; }

    /// <summary>Individual security findings from static analysis.</summary>
    public required List<SecurityFinding> Findings { get; init; }

    /// <summary>OWASP Top 10 (2021) category coverage scores.</summary>
    public required OwaspCoverage Owasp { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (SecretHygiene, 0.20),
        (AuthPatterns, 0.15),
        (InputValidation, 0.15),
        (CryptographyPractice, 0.10),
        (DependencySecurity, 0.15),
        (HttpSecurity, 0.10),
        (DataProtection, 0.15)
    );
}

/// <summary>
/// A single security finding from static analysis or manual review.
/// </summary>
public sealed record SecurityFinding
{
    /// <summary>Rule identifier for the finding.</summary>
    public required string RuleId { get; init; }

    /// <summary>Security category (e.g. "Injection", "Authentication").</summary>
    public required string Category { get; init; }

    /// <summary>Severity level (e.g. "Critical", "High", "Medium", "Low").</summary>
    public required string Severity { get; init; }

    /// <summary>Path to the file containing the finding.</summary>
    public required string FilePath { get; init; }

    /// <summary>Line number in the file, if available.</summary>
    public int? LineNumber { get; init; }

    /// <summary>Human-readable description of the finding.</summary>
    public required string Description { get; init; }

    /// <summary>Recommended remediation action.</summary>
    public required string Recommendation { get; init; }
}

/// <summary>
/// OWASP Top 10 (2021) category coverage — each scored 0.0–1.0.
/// </summary>
public sealed record OwaspCoverage
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A01: Broken Access Control.</summary>
    public required double BrokenAccessControl { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A02: Cryptographic Failures.</summary>
    public required double CryptographicFailures { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A03: Injection.</summary>
    public required double Injection { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A04: Insecure Design.</summary>
    public required double InsecureDesign { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A05: Security Misconfiguration.</summary>
    public required double SecurityMisconfiguration { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A06: Vulnerable Components.</summary>
    public required double VulnerableComponents { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A07: Authentication Failures.</summary>
    public required double AuthenticationFailures { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A08: Data Integrity Failures.</summary>
    public required double DataIntegrityFailures { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A09: Logging/Monitoring Failures.</summary>
    public required double LoggingMonitoringFailures { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for A10: Server-Side Request Forgery.</summary>
    public required double ServerSideRequestForgery { get; init; }

    /// <summary>Average score across all OWASP Top 10 categories.</summary>
    public double AverageScore => new[]
    {
        BrokenAccessControl, CryptographicFailures, Injection,
        InsecureDesign, SecurityMisconfiguration, VulnerableComponents,
        AuthenticationFailures, DataIntegrityFailures,
        LoggingMonitoringFailures, ServerSideRequestForgery
    }.Average();
}
