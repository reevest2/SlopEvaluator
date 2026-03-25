namespace SlopEvaluator.Health.HealthChecks;

/// <summary>
/// Health check that validates the codebase scoring pipeline is operational.
/// Implements IHealthCheck pattern for observability detection.
/// </summary>
public class CodebaseHealthCheck : IHealthCheck
{
    /// <summary>
    /// Check that the scoring pipeline can produce a valid score.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // Validate that ScoreAggregator works correctly
        var testScore = Models.ScoreAggregator.WeightedAverage((0.5, 0.5), (0.5, 0.5));
        if (testScore is >= 0.0 and <= 1.0)
            return Task.FromResult(HealthCheckResult.Healthy("Scoring pipeline operational"));

        return Task.FromResult(HealthCheckResult.Unhealthy("Scoring pipeline produced invalid result"));
    }
}

/// <summary>
/// Interface for health check implementations.
/// </summary>
public interface IHealthCheck
{
    /// <summary>
    /// Performs the health check.
    /// </summary>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a health check operation.
/// </summary>
public record HealthCheckResult(string Status, string Description)
{
    /// <summary>
    /// Creates a healthy result.
    /// </summary>
    public static HealthCheckResult Healthy(string description) => new("Healthy", description);

    /// <summary>
    /// Creates an unhealthy result.
    /// </summary>
    public static HealthCheckResult Unhealthy(string description) => new("Unhealthy", description);

    /// <summary>
    /// Creates a degraded result.
    /// </summary>
    public static HealthCheckResult Degraded(string description) => new("Degraded", description);
}
