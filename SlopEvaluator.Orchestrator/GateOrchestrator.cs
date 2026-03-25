using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SlopEvaluator.Orchestrator;

/// <summary>
/// CI quality gate: runs a health scan and compares against a threshold.
/// Returns pass/fail for use in CI pipelines.
/// </summary>
public sealed class GateOrchestrator
{
    private readonly ILoggerFactory _loggerFactory;

    public GateOrchestrator(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Run a health scan and check whether the score meets the threshold.
    /// </summary>
    /// <param name="path">Path to the project root.</param>
    /// <param name="threshold">Minimum acceptable health score (0.0 to 1.0).</param>
    /// <param name="name">Optional codebase name.</param>
    /// <returns>Whether the gate passed and the actual score.</returns>
    public async Task<(bool Passed, double Score)> CheckAsync(
        string path, double threshold, string? name = null)
    {
        var scanner = new ScanOrchestrator(_loggerFactory);
        var (codebase, _) = await scanner.ScanAsync(path, name);

        bool passed = codebase.Score >= threshold;
        return (passed, codebase.Score);
    }
}
