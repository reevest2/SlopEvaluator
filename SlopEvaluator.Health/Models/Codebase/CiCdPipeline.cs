namespace SlopEvaluator.Health.Models;

/// <summary>
/// Build reliability, DORA metrics, pipeline stages, and deployment health.
/// </summary>
public sealed record CiCdPipeline : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for build reliability.</summary>
    public required double BuildReliability { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for build speed.</summary>
    public required double BuildSpeed { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for deployment frequency.</summary>
    public required double DeployFrequency { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for pipeline stage completeness.</summary>
    public required double PipelineCompleteness { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for environment parity.</summary>
    public required double EnvironmentParity { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for rollback capability.</summary>
    public required double RollbackCapability { get; init; }

    /// <summary>Build timing and success rate metrics.</summary>
    public required BuildProfile Build { get; init; }

    /// <summary>Deployment frequency, DORA metrics, and release strategy.</summary>
    public required DeploymentProfile Deployment { get; init; }

    /// <summary>Individual pipeline stages and their automation status.</summary>
    public required List<PipelineStage> Stages { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (BuildReliability, 0.20),
        (BuildSpeed, 0.10),
        (DeployFrequency, 0.15),
        (PipelineCompleteness, 0.25),
        (EnvironmentParity, 0.15),
        (RollbackCapability, 0.15)
    );
}

/// <summary>
/// CI build timing, success rate, and caching configuration.
/// </summary>
public sealed record BuildProfile
{
    /// <summary>Average CI build wall-clock time.</summary>
    public required TimeSpan AverageBuildTime { get; init; }

    /// <summary>Median CI build wall-clock time.</summary>
    public required TimeSpan MedianBuildTime { get; init; }

    /// <summary>Build success rate (0.0 to 1.0).</summary>
    public required double SuccessRate { get; init; }

    /// <summary>Number of CI builds triggered per week.</summary>
    public required int BuildsPerWeek { get; init; }

    /// <summary>Trend in build warnings (positive = increasing).</summary>
    public required int WarningTrend { get; init; }

    /// <summary>Whether incremental builds are enabled in CI.</summary>
    public required bool IncrementalBuildEnabled { get; init; }

    /// <summary>Whether build caching is enabled in CI.</summary>
    public required bool CacheEnabled { get; init; }
}

/// <summary>
/// Deployment frequency, DORA metrics, and release strategy details.
/// </summary>
public sealed record DeploymentProfile
{
    /// <summary>Average number of production deployments per week.</summary>
    public required double DeploysPerWeek { get; init; }

    /// <summary>DORA: median lead time from commit to production.</summary>
    public required TimeSpan LeadTimeForChanges { get; init; }

    /// <summary>DORA: mean time to recover from a production incident.</summary>
    public required TimeSpan MeanTimeToRecover { get; init; }

    /// <summary>DORA: percentage of deployments causing a failure (0.0 to 1.0).</summary>
    public required double ChangeFailureRate { get; init; }

    /// <summary>Number of deployment environments.</summary>
    public required int Environments { get; init; }

    /// <summary>Names of the deployment environments.</summary>
    public required List<string> EnvironmentNames { get; init; }

    /// <summary>Whether blue-green or canary deployment strategy is used.</summary>
    public required bool BlueGreenOrCanary { get; init; }

    /// <summary>Whether automated rollback is configured.</summary>
    public required bool AutomatedRollback { get; init; }
}

/// <summary>
/// A single stage in the CI/CD pipeline.
/// </summary>
public sealed record PipelineStage
{
    /// <summary>Display name of the pipeline stage.</summary>
    public required string Name { get; init; }

    /// <summary>Type classification of the pipeline stage.</summary>
    public required PipelineStageType Type { get; init; }

    /// <summary>Whether this stage is present in the pipeline.</summary>
    public required bool Present { get; init; }

    /// <summary>Whether this stage runs automatically (not manual).</summary>
    public required bool Automated { get; init; }

    /// <summary>Average wall-clock duration of this stage, if measured.</summary>
    public TimeSpan? AverageDuration { get; init; }
}

/// <summary>
/// Classification of CI/CD pipeline stage types.
/// </summary>
public enum PipelineStageType
{
    /// <summary>Build compilation stage.</summary>
    Build,
    /// <summary>Unit test execution stage.</summary>
    UnitTest,
    /// <summary>Integration test execution stage.</summary>
    IntegrationTest,
    /// <summary>Security scanning stage.</summary>
    SecurityScan,
    /// <summary>Static code analysis stage.</summary>
    CodeAnalysis,
    /// <summary>Deployment to an environment.</summary>
    Deploy,
    /// <summary>Post-deployment smoke test stage.</summary>
    SmokeTest,
    /// <summary>Performance and load testing stage.</summary>
    PerformanceTest,
    /// <summary>Manual approval gate.</summary>
    Approval
}
