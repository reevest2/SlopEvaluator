namespace SlopEvaluator.Health.HealthChecks;

/// <summary>
/// Health check that validates storage is accessible.
/// Implements IHealthCheck for observability detection.
/// </summary>
public class StorageHealthCheck : IHealthCheck
{
    private readonly string _dataDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageHealthCheck"/> class.
    /// </summary>
    /// <param name="dataDirectory">Directory to check for accessibility.</param>
    public StorageHealthCheck(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    /// <summary>
    /// Check whether the storage directory exists and is accessible.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(_dataDirectory))
            return Task.FromResult(HealthCheckResult.Healthy($"Storage accessible: {_dataDirectory}"));

        return Task.FromResult(HealthCheckResult.Degraded($"Storage directory missing: {_dataDirectory}"));
    }
}
