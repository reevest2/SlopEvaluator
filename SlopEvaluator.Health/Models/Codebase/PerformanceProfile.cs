namespace SlopEvaluator.Health.Models;

/// <summary>
/// Startup time, memory efficiency, latency, throughput, GC pressure, and benchmarks.
/// </summary>
public sealed record PerformanceProfile : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for startup performance.</summary>
    public required double StartupPerformance { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for memory efficiency.</summary>
    public required double MemoryEfficiency { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for response latency.</summary>
    public required double ResponseLatency { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for throughput capacity.</summary>
    public required double ThroughputCapacity { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for allocation efficiency.</summary>
    public required double AllocationEfficiency { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for published bundle/output size.</summary>
    public required double BundleSize { get; init; }

    /// <summary>Cold and warm startup timing and memory measurements.</summary>
    public required StartupMetrics Startup { get; init; }

    /// <summary>Runtime GC pressure, memory, and thread pool metrics.</summary>
    public required RuntimeMetrics Runtime { get; init; }

    /// <summary>BenchmarkDotNet results for key operations.</summary>
    public required List<BenchmarkResult> Benchmarks { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (StartupPerformance, 0.15),
        (MemoryEfficiency, 0.20),
        (ResponseLatency, 0.25),
        (ThroughputCapacity, 0.15),
        (AllocationEfficiency, 0.15),
        (BundleSize, 0.10)
    );
}

/// <summary>
/// Application startup timing and memory measurements.
/// </summary>
public sealed record StartupMetrics
{
    /// <summary>Time from process start to ready on a cold start.</summary>
    public required TimeSpan ColdStartTime { get; init; }

    /// <summary>Time from process start to ready on a warm start.</summary>
    public required TimeSpan WarmStartTime { get; init; }

    /// <summary>Peak memory usage in bytes during startup.</summary>
    public required long PeakStartupMemoryBytes { get; init; }

    /// <summary>Number of assemblies loaded during startup.</summary>
    public required int AssembliesLoaded { get; init; }
}

/// <summary>
/// Runtime memory, GC, and thread pool metrics.
/// </summary>
public sealed record RuntimeMetrics
{
    /// <summary>Working set size in bytes at steady state.</summary>
    public required long WorkingSetBytes { get; init; }

    /// <summary>Gen 0 garbage collections per minute.</summary>
    public required double Gen0CollectionsPerMinute { get; init; }

    /// <summary>Gen 1 garbage collections per minute.</summary>
    public required double Gen1CollectionsPerMinute { get; init; }

    /// <summary>Gen 2 garbage collections per minute.</summary>
    public required double Gen2CollectionsPerMinute { get; init; }

    /// <summary>Bytes allocated per second at steady state.</summary>
    public required long AllocatedBytesPerSecond { get; init; }

    /// <summary>Thread pool utilization ratio (0.0 to 1.0).</summary>
    public required double ThreadPoolUtilization { get; init; }
}

/// <summary>
/// A single BenchmarkDotNet result for a key operation.
/// </summary>
public sealed record BenchmarkResult
{
    /// <summary>Name of the benchmark.</summary>
    public required string Name { get; init; }

    /// <summary>Category grouping for the benchmark.</summary>
    public required string Category { get; init; }

    /// <summary>Mean execution time in nanoseconds.</summary>
    public required double MeanNanoseconds { get; init; }

    /// <summary>Median execution time in nanoseconds.</summary>
    public required double MedianNanoseconds { get; init; }

    /// <summary>Bytes allocated per operation.</summary>
    public required long AllocatedBytes { get; init; }

    /// <summary>Standard deviation of execution time in nanoseconds.</summary>
    public required double StandardDeviation { get; init; }

    /// <summary>Baseline mean time in nanoseconds for regression comparison.</summary>
    public double? BaselineMeanNanoseconds { get; init; }

    /// <summary>Percentage regression from baseline, if available.</summary>
    public double? RegressionPercent { get; init; }
}
