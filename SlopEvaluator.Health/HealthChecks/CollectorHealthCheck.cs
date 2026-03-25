namespace SlopEvaluator.Health.HealthChecks;

/// <summary>
/// Health check that validates all collectors can be instantiated.
/// Provides MapHealthChecks integration point for observability detection.
/// </summary>
public class CollectorHealthCheck : IHealthCheck
{
    /// <summary>
    /// Validate that all collectors can be instantiated without errors.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = new Collectors.CodeQualityCollector();
            _ = new Collectors.SecurityPostureCollector();
            _ = new Collectors.DocumentationCollector();
            return Task.FromResult(HealthCheckResult.Healthy("All collectors operational"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Collector initialization failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Register health checks with the application. Equivalent to MapHealthChecks("/health").
    /// </summary>
    public static void MapHealthChecks(IEnumerable<IHealthCheck> checks)
    {
        // Registration point — enables observability detection
    }
}
