using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Measures build times, tooling presence, and inner loop speed.
/// </summary>
public class DeveloperExperienceCollector
{
    private readonly ILogger<DeveloperExperienceCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeveloperExperienceCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public DeveloperExperienceCollector(ILogger<DeveloperExperienceCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<DeveloperExperienceCollector>.Instance;
    }

    /// <summary>
    /// Measure build times, tooling presence, and inner loop speed for a project.
    /// </summary>
    public async Task<DeveloperExperience> CollectAsync(string projectPath)
    {
        _logger.LogDebug("Starting developer experience analysis for {ProjectPath}", projectPath);
        var tooling = ScanTooling(projectPath);
        var innerLoop = await MeasureInnerLoopAsync(projectPath);

        double buildTimeSatisfaction = ScoreBuildTime(innerLoop.CleanBuildTime);
        double testRunSpeed = ScoreBuildTime(innerLoop.UnitTestRunTime);
        _logger.LogInformation("DevEx scores: buildTime={BuildScore:F3}, tooling={ToolCount} tools detected", buildTimeSatisfaction, tooling.ConfiguredTools.Count);
        double onboardingFriction = ScoreOnboardingFriction(tooling, innerLoop);
        double toolingMaturity = ScoreToolingMaturity(tooling);
        double innerLoopSpeed = ScoreInnerLoopSpeed(innerLoop);
        double debugExperience = ScoreDebugExperience(projectPath);

        return new DeveloperExperience
        {
            BuildTimeSatisfaction = buildTimeSatisfaction,
            TestRunSpeed = testRunSpeed,
            OnboardingFriction = onboardingFriction,
            ToolingMaturity = toolingMaturity,
            InnerLoopSpeed = innerLoopSpeed,
            DebugExperience = debugExperience,
            Tooling = tooling,
            InnerLoop = innerLoop
        };
    }

    /// <summary>
    /// Scan for development tooling configuration files in the project.
    /// </summary>
    internal static ToolingProfile ScanTooling(string path)
    {
        var tools = new List<string>();

        bool editorConfig = File.Exists(Path.Combine(path, ".editorconfig"));
        bool launchSettings = Directory.GetFiles(path, "launchSettings.json", SearchOption.AllDirectories).Length > 0;
        bool dirBuildProps = File.Exists(Path.Combine(path, "Directory.Build.props"));
        bool dockerfile = File.Exists(Path.Combine(path, "Dockerfile"))
            || File.Exists(Path.Combine(path, "docker-compose.yml"))
            || File.Exists(Path.Combine(path, "docker-compose.yaml"));
        bool devContainer = Directory.Exists(Path.Combine(path, ".devcontainer"));
        bool gitHooks = Directory.Exists(Path.Combine(path, ".husky"))
            || Directory.Exists(Path.Combine(path, ".githooks"));

        if (editorConfig) tools.Add("editorconfig");
        if (dirBuildProps) tools.Add("Directory.Build.props");
        if (dockerfile) tools.Add("docker");
        if (devContainer) tools.Add("devcontainer");
        if (gitHooks) tools.Add("git-hooks");
        if (File.Exists(Path.Combine(path, "global.json"))) tools.Add("global.json");
        if (Directory.Exists(Path.Combine(path, ".github"))) tools.Add("github-actions");
        if (Directory.Exists(Path.Combine(path, ".claude"))) tools.Add("claude-code");

        // Check for common .NET tools
        var csprojs = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
        foreach (var csproj in csprojs.Take(5))
        {
            var content = File.ReadAllText(csproj).ToLowerInvariant();
            if (content.Contains("coverlet")) tools.Add("coverlet");
            if (content.Contains("benchmark")) tools.Add("benchmarkdotnet");
            break; // just check first for speed
        }

        return new ToolingProfile
        {
            EditorConfigPresent = editorConfig,
            LaunchSettingsPresent = launchSettings,
            DirectoryBuildPropsPresent = dirBuildProps,
            DockerfilePresent = dockerfile,
            DevContainerPresent = devContainer,
            GitHooksConfigured = gitHooks,
            ConfiguredTools = tools.Distinct().ToList()
        };
    }

    /// <summary>
    /// Measure build, incremental build, and test run times for the inner development loop.
    /// </summary>
    internal static async Task<InnerLoopProfile> MeasureInnerLoopAsync(string path)
    {
        var cleanBuild = await TimeDotnetCommandAsync(path, "build --no-incremental");
        var incrementalBuild = await TimeDotnetCommandAsync(path, "build");
        var testRun = await TimeDotnetCommandAsync(path, "test --no-build");

        bool hotReload = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)
            .Take(3)
            .Any(f => File.ReadAllText(f).Contains("Microsoft.NET.Sdk.Web")
                    || File.ReadAllText(f).Contains("Microsoft.NET.Sdk.BlazorWebAssembly"));

        bool watchMode = true; // .NET always supports dotnet watch

        // Steps to first run: git clone + restore + build + run
        int steps = 3; // minimum: clone, restore, run
        if (!File.Exists(Path.Combine(path, "global.json"))) steps++;
        if (File.Exists(Path.Combine(path, "docker-compose.yml"))) steps++; // need docker too

        return new InnerLoopProfile
        {
            CleanBuildTime = cleanBuild,
            IncrementalBuildTime = incrementalBuild,
            UnitTestRunTime = testRun,
            HotReloadSupported = hotReload,
            WatchModeAvailable = watchMode,
            StepsToFirstRun = steps
        };
    }

    private static async Task<TimeSpan> TimeDotnetCommandAsync(string path, string args)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args,
                WorkingDirectory = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return TimeSpan.FromMinutes(5);

            // Read both streams to prevent deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var completed = await Task.WhenAny(
                process.WaitForExitAsync(),
                Task.Delay(TimeSpan.FromMinutes(3)));

            sw.Stop();

            if (!process.HasExited)
            {
                process.Kill();
                return TimeSpan.FromMinutes(3);
            }

            return sw.Elapsed;
        }
        catch
        {
            return TimeSpan.FromMinutes(5);
        }
    }

    /// <summary>
    /// Build time → 0-1. Under 10s = 1.0, over 5min = 0.0.
    /// </summary>
    internal static double ScoreBuildTime(TimeSpan time)
    {
        if (time.TotalSeconds <= 10) return 1.0;
        if (time.TotalSeconds >= 300) return 0.0;
        return 1.0 - (time.TotalSeconds - 10) / 290.0;
    }

    /// <summary>
    /// Score tooling maturity based on presence of key development configuration files.
    /// </summary>
    internal static double ScoreToolingMaturity(ToolingProfile tooling)
    {
        int checks = 0, passed = 0;

        checks++; if (tooling.EditorConfigPresent) passed++;
        checks++; if (tooling.LaunchSettingsPresent) passed++;
        checks++; if (tooling.DirectoryBuildPropsPresent) passed++;
        checks++; if (tooling.DockerfilePresent) passed++;
        checks++; if (tooling.GitHooksConfigured) passed++;
        checks++; if (tooling.ConfiguredTools.Count >= 3) passed++;

        return (double)passed / checks;
    }

    /// <summary>
    /// Score onboarding friction inversely: fewer steps and faster builds yield higher scores.
    /// </summary>
    internal static double ScoreOnboardingFriction(ToolingProfile tooling, InnerLoopProfile innerLoop)
    {
        // Lower friction = higher score (inverted)
        double stepsScore = innerLoop.StepsToFirstRun <= 3 ? 1.0
            : innerLoop.StepsToFirstRun <= 5 ? 0.7
            : 0.4;

        double buildScore = ScoreBuildTime(innerLoop.CleanBuildTime);
        double dockerScore = tooling.DockerfilePresent ? 1.0 : 0.5;

        return (stepsScore * 0.4 + buildScore * 0.3 + dockerScore * 0.3);
    }

    /// <summary>
    /// Score inner loop speed based on incremental build and test run times.
    /// </summary>
    internal static double ScoreInnerLoopSpeed(InnerLoopProfile innerLoop)
    {
        double buildScore = ScoreBuildTime(innerLoop.IncrementalBuildTime);
        double testScore = ScoreBuildTime(innerLoop.UnitTestRunTime);
        double hotReloadBonus = innerLoop.HotReloadSupported ? 0.1 : 0;

        return Math.Min(1.0, (buildScore * 0.5 + testScore * 0.5) + hotReloadBonus);
    }

    /// <summary>
    /// Score debugging experience based on presence of launch configurations and IDE support.
    /// </summary>
    internal static double ScoreDebugExperience(string path)
    {
        int checks = 0, passed = 0;

        // Launch settings present
        checks++;
        if (Directory.GetFiles(path, "launchSettings.json", SearchOption.AllDirectories).Length > 0)
            passed++;

        // .vscode/launch.json or .vs present
        checks++;
        if (Directory.Exists(Path.Combine(path, ".vscode"))
            || File.Exists(Path.Combine(path, ".vs", "launch.json")))
            passed++;

        // Directory.Build.props with debug config
        checks++;
        var dbp = Path.Combine(path, "Directory.Build.props");
        if (File.Exists(dbp) && File.ReadAllText(dbp).Contains("Debug"))
            passed++;

        return checks > 0 ? (double)passed / checks : 0.5;
    }
}
