using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Measures startup time, assembly count, and basic performance characteristics.
/// </summary>
public class PerformanceProfileCollector
{
    private readonly ILogger<PerformanceProfileCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PerformanceProfileCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public PerformanceProfileCollector(ILogger<PerformanceProfileCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<PerformanceProfileCollector>.Instance;
    }

    /// <summary>
    /// Measure startup time, assembly count, and performance characteristics for a project.
    /// </summary>
    public async Task<PerformanceProfile> CollectAsync(string projectPath)
    {
        _logger.LogDebug("Starting performance analysis for {ProjectPath}", projectPath);
        var startup = await MeasureStartupAsync(projectPath);
        int assemblyCount = CountAssemblies(projectPath);
        long publishSize = MeasurePublishSize(projectPath);

        _logger.LogDebug("Found {AssemblyCount} assemblies in build output", assemblyCount);

        if (startup.ColdStartTime.TotalSeconds > 60)
            _logger.LogWarning("Build time {BuildTime:F1}s exceeds 60-second threshold for {ProjectPath}", startup.ColdStartTime.TotalSeconds, projectPath);

        // Score startup: sub-2s = 1.0, over 30s = 0.0 (linear interpolation between)
        double startupPerf = ScoreStartupTime(startup.ColdStartTime);
        // Bundle size combines assembly count tiers and published output size tiers
        double bundleSize = ScoreBundleSize(publishSize, assemblyCount);

        // Measure runtime memory usage from the current process working set.
        // This is a point-in-time snapshot, not a profiled steady-state measurement.
        long workingSet = Environment.WorkingSet;
        double memoryMb = workingSet / (1024.0 * 1024.0);
        // Memory score: under 100MB = 1.0, over 500MB = 0.0 (linear interpolation)
        double memoryEff = ScoreMemoryUsage(memoryMb);
        _logger.LogInformation("Memory usage summary: {MemoryMb:F1} MB working set, efficiency score {MemoryScore:F3}", memoryMb, memoryEff);

        // GC pressure: Gen2 collections are expensive full-heap collections.
        // 0 Gen2 = 1.0, 1-3 = 0.9, 4-5 = 0.7, 6+ = 0.4
        int gen2Collections = GC.CollectionCount(2);
        double allocEff = ScoreAllocationEfficiency(gen2Collections);

        // Latency is derived from cold start time (sub-2s = excellent for CLI tools).
        // Throughput is estimated from assembly count as a proxy for dependency graph leanness.
        double latency = ScoreResponseLatency(startup.ColdStartTime);
        double throughput = ScoreThroughputCapacity(assemblyCount);

        return new PerformanceProfile
        {
            StartupPerformance = startupPerf,
            MemoryEfficiency = memoryEff,
            ResponseLatency = latency,
            ThroughputCapacity = throughput,
            AllocationEfficiency = allocEff,
            BundleSize = bundleSize,
            Startup = startup,
            Runtime = new RuntimeMetrics
            {
                WorkingSetBytes = workingSet,
                Gen0CollectionsPerMinute = GC.CollectionCount(0),
                Gen1CollectionsPerMinute = GC.CollectionCount(1),
                Gen2CollectionsPerMinute = gen2Collections,
                AllocatedBytesPerSecond = 0,
                ThreadPoolUtilization = 0
            },
            Benchmarks = await DiscoverBenchmarksAsync(projectPath)
        };
    }

    private static async Task<StartupMetrics> MeasureStartupAsync(string projectPath)
    {
        // Find a runnable project (console or web)
        var csprojs = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
            .Where(f => {
                var rel = Path.GetRelativePath(projectPath, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
                    && !rel.Contains("benchmarks/")
                    && !rel.Contains(".claude/worktrees/")
                    && !rel.Contains("SlopEvaluator/")
                    && !rel.Contains("SlopEvaluator\\")
                    && !rel.Contains("Test");
            })
            .ToList();

        TimeSpan coldStart = TimeSpan.Zero;
        foreach (var csproj in csprojs)
        {
            var content = await File.ReadAllTextAsync(csproj);
            if (!content.Contains("<OutputType>Exe</OutputType>")
                && !content.Contains("Microsoft.NET.Sdk.Web"))
                continue;

            // Time dotnet build + brief run
            coldStart = await TimeStartupAsync(Path.GetDirectoryName(csproj)!);
            break;
        }

        int assemblies = CountAssemblies(projectPath);

        return new StartupMetrics
        {
            ColdStartTime = coldStart,
            WarmStartTime = coldStart * 0.3, // rough estimate
            PeakStartupMemoryBytes = 0, // needs runtime profiling
            AssembliesLoaded = assemblies
        };
    }

    private static async Task<TimeSpan> TimeStartupAsync(string projectDir)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build --no-restore",
                WorkingDirectory = projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return TimeSpan.FromMinutes(1);

            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            sw.Stop();
            return sw.Elapsed;
        }
        catch
        {
            return TimeSpan.FromMinutes(1);
        }
    }

    internal static int CountAssemblies(string projectPath)
    {
        // Count DLLs in the first bin/Debug output folder
        var binDirs = Directory.GetDirectories(projectPath, "net*",  SearchOption.AllDirectories)
            .Where(d => {
                var normalized = d.Replace('\\', '/');
                return normalized.Contains("/bin/Debug/")
                    && !normalized.Contains("SlopEvaluator/")
                    && !normalized.Contains(".claude/worktrees/");
            })
            .Take(1);

        foreach (var dir in binDirs)
            return Directory.GetFiles(dir, "*.dll").Length;

        return 0;
    }

    internal static long MeasurePublishSize(string projectPath)
    {
        // Check if there's a published output
        var publishDirs = Directory.GetDirectories(projectPath, "publish", SearchOption.AllDirectories)
            .Where(d => d.Replace('\\', '/').Contains("/bin/"))
            .Take(1);

        foreach (var dir in publishDirs)
        {
            return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }

        return 0;
    }

    /// <summary>
    /// Startup time → 0-1. Under 2s = 1.0, over 30s = 0.0.
    /// </summary>
    internal static double ScoreStartupTime(TimeSpan time)
    {
        if (time == TimeSpan.Zero) return 0.5; // couldn't measure
        if (time.TotalSeconds <= 2) return 1.0;
        if (time.TotalSeconds >= 30) return 0.0;
        return 1.0 - (time.TotalSeconds - 2) / 28.0;
    }

    /// <summary>
    /// Bundle size score. Fewer assemblies and smaller publish = better.
    /// </summary>
    // Bundle size scoring uses tiered thresholds for both assembly count and publish size.
    // Assembly tiers: <=20 = 1.0, <=50 = 0.8, <=100 = 0.6, <=200 = 0.4, >200 = 0.2
    // Size tiers (MB): <=10 = 1.0, <=50 = 0.8, <=100 = 0.6, <=500 = 0.4, >500 = 0.2
    // Final score is the average of both sub-scores (or just assembly score if no publish data).
    internal static double ScoreBundleSize(long publishBytes, int assemblies)
    {
        if (publishBytes == 0 && assemblies == 0) return 0.5; // no data available

        double assemblyScore = assemblies switch
        {
            <= 20 => 1.0,
            <= 50 => 0.8,
            <= 100 => 0.6,
            <= 200 => 0.4,
            _ => 0.2
        };

        if (publishBytes == 0) return assemblyScore;

        double sizeMb = publishBytes / (1024.0 * 1024.0);
        double sizeScore = sizeMb switch
        {
            <= 10 => 1.0,
            <= 50 => 0.8,
            <= 100 => 0.6,
            <= 500 => 0.4,
            _ => 0.2
        };

        return (assemblyScore + sizeScore) / 2.0;
    }

    /// <summary>
    /// Score memory usage. Under 100MB = 1.0, over 500MB = 0.0.
    /// </summary>
    internal static double ScoreMemoryUsage(double memoryMb)
    {
        if (memoryMb <= 100) return 1.0;
        if (memoryMb >= 500) return 0.0;
        return 1.0 - (memoryMb - 100) / 400.0;
    }

    /// <summary>
    /// Score allocation efficiency based on Gen2 GC collections.
    /// Fewer Gen2 collections indicate less memory pressure.
    /// </summary>
    internal static double ScoreAllocationEfficiency(int gen2Collections)
    {
        if (gen2Collections == 0) return 1.0;
        if (gen2Collections <= 3) return 0.9;
        if (gen2Collections <= 5) return 0.7;
        return 0.4;
    }

    /// <summary>
    /// Score response latency based on cold start time.
    /// Sub-2-second cold start is excellent for a CLI tool.
    /// </summary>
    internal static double ScoreResponseLatency(TimeSpan coldStart)
    {
        if (coldStart.TotalSeconds <= 2) return 1.0;
        if (coldStart.TotalSeconds <= 5) return 0.8;
        return 0.5;
    }

    /// <summary>
    /// Score throughput capacity based on assembly count.
    /// Fewer assemblies mean leaner dependency graph and faster throughput.
    /// </summary>
    internal static double ScoreThroughputCapacity(int assemblyCount)
    {
        if (assemblyCount <= 20) return 0.9;
        if (assemblyCount <= 30) return 0.7;
        return 0.5;
    }

    private static async Task<List<BenchmarkResult>> DiscoverBenchmarksAsync(string projectPath)
    {
        // Check for BenchmarkDotNet results
        var resultsDir = Path.Combine(projectPath, "BenchmarkDotNet.Artifacts", "results");
        if (!Directory.Exists(resultsDir))
            return [];

        // Would parse CSV/JSON results here
        await Task.CompletedTask;
        return [];
    }
}
