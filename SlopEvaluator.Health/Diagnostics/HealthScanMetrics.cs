using System.Diagnostics.Metrics;

namespace SlopEvaluator.Health.Diagnostics;

/// <summary>
/// OpenTelemetry-compatible metrics for health scan operations.
/// Uses System.Diagnostics.Metrics for Counter, Histogram, and Gauge instrumentation.
/// </summary>
public static class HealthScanMetrics
{
    private static readonly Meter Meter = new("SlopEvaluator.Health", "1.0.0");

    /// <summary>
    /// Counter tracking the total number of completed health scans.
    /// </summary>
    public static readonly Counter<long> ScansCompleted = Meter.CreateCounter<long>(
        "health.scans.completed",
        description: "Total number of health scans completed");

    /// <summary>
    /// Histogram tracking the duration of health scan operations in milliseconds.
    /// </summary>
    public static readonly Histogram<double> ScanDuration = Meter.CreateHistogram<double>(
        "health.scan.duration_ms",
        unit: "ms",
        description: "Duration of health scan operations");

    private static double _currentScore;

    /// <summary>
    /// Observable gauge reporting the most recent health score.
    /// </summary>
    public static readonly ObservableGauge<double> CurrentScore = Meter.CreateObservableGauge(
        "health.current.score",
        () => _currentScore,
        description: "Most recent codebase health score");

    /// <summary>
    /// Records the latest health score for the observable gauge.
    /// </summary>
    public static void RecordScore(double score) => _currentScore = score;
}
