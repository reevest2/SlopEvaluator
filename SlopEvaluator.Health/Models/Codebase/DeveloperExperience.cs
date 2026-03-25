namespace SlopEvaluator.Health.Models;

/// <summary>
/// Build times, inner loop speed, onboarding friction, and tooling maturity.
/// </summary>
public sealed record DeveloperExperience : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for build time satisfaction.</summary>
    public required double BuildTimeSatisfaction { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for test run speed.</summary>
    public required double TestRunSpeed { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for low onboarding friction.</summary>
    public required double OnboardingFriction { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for tooling maturity.</summary>
    public required double ToolingMaturity { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for inner development loop speed.</summary>
    public required double InnerLoopSpeed { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for debugging experience quality.</summary>
    public required double DebugExperience { get; init; }

    /// <summary>Presence of development tooling configuration files.</summary>
    public required ToolingProfile Tooling { get; init; }

    /// <summary>Build and test timing measurements for the inner dev loop.</summary>
    public required InnerLoopProfile InnerLoop { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (BuildTimeSatisfaction, 0.20),
        (TestRunSpeed, 0.15),
        (OnboardingFriction, 0.15),
        (ToolingMaturity, 0.20),
        (InnerLoopSpeed, 0.15),
        (DebugExperience, 0.15)
    );
}

/// <summary>
/// Presence of development tooling and configuration files.
/// </summary>
public sealed record ToolingProfile
{
    /// <summary>Whether an .editorconfig file is present.</summary>
    public required bool EditorConfigPresent { get; init; }

    /// <summary>Whether launchSettings.json is present.</summary>
    public required bool LaunchSettingsPresent { get; init; }

    /// <summary>Whether Directory.Build.props is present.</summary>
    public required bool DirectoryBuildPropsPresent { get; init; }

    /// <summary>Whether a Dockerfile is present.</summary>
    public required bool DockerfilePresent { get; init; }

    /// <summary>Whether a dev container configuration is present.</summary>
    public required bool DevContainerPresent { get; init; }

    /// <summary>Whether git hooks are configured (e.g. via Husky).</summary>
    public required bool GitHooksConfigured { get; init; }

    /// <summary>List of configured development tools.</summary>
    public required List<string> ConfiguredTools { get; init; }
}

/// <summary>
/// Inner development loop timing measurements.
/// </summary>
public sealed record InnerLoopProfile
{
    /// <summary>Wall-clock time for a clean (from-scratch) build.</summary>
    public required TimeSpan CleanBuildTime { get; init; }

    /// <summary>Wall-clock time for an incremental build after a small change.</summary>
    public required TimeSpan IncrementalBuildTime { get; init; }

    /// <summary>Wall-clock time to run the unit test suite.</summary>
    public required TimeSpan UnitTestRunTime { get; init; }

    /// <summary>Whether hot reload is supported for rapid iteration.</summary>
    public required bool HotReloadSupported { get; init; }

    /// <summary>Whether dotnet watch mode is available.</summary>
    public required bool WatchModeAvailable { get; init; }

    /// <summary>Number of manual steps required to run the application for the first time.</summary>
    public required int StepsToFirstRun { get; init; }
}
