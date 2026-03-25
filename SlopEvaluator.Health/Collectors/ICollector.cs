namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Defines the contract for codebase health dimension collectors.
/// </summary>
/// <typeparam name="T">The measurement result type produced by this collector.</typeparam>
public interface ICollector<T>
{
    /// <summary>
    /// Collects metrics for a specific dimension of codebase health.
    /// </summary>
    /// <param name="projectPath">Root path of the project to analyze.</param>
    /// <param name="codebaseName">Human-readable name of the codebase.</param>
    /// <returns>A task that resolves to the measurement result.</returns>
    Task<T> CollectAsync(string projectPath, string codebaseName);
}
