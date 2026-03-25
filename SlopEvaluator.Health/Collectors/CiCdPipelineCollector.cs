using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Scans CI/CD workflow files (GitHub Actions, Azure DevOps) and build configuration.
/// </summary>
public class CiCdPipelineCollector
{
    private readonly ILogger<CiCdPipelineCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CiCdPipelineCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public CiCdPipelineCollector(ILogger<CiCdPipelineCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<CiCdPipelineCollector>.Instance;
    }

    /// <summary>
    /// Scan CI/CD workflow files and build configuration to produce a pipeline health score.
    /// </summary>
    public async Task<CiCdPipeline> CollectAsync(string projectPath)
    {
        _logger.LogDebug("Starting CI/CD pipeline scan for {ProjectPath}", projectPath);
        var stages = new List<PipelineStage>();
        var workflowContent = await ReadWorkflowFilesAsync(projectPath);

        var workflowDir = Path.Combine(projectPath, ".github", "workflows");
        if (string.IsNullOrEmpty(workflowContent))
            _logger.LogWarning("No CI/CD workflow files found in {Path}", workflowDir);

        // Detect stages from workflow content
        stages.Add(DetectStage("Build", PipelineStageType.Build, workflowContent,
            ["dotnet build", "msbuild", "dotnet publish"]));
        stages.Add(DetectStage("Unit Tests", PipelineStageType.UnitTest, workflowContent,
            ["dotnet test", "xunit", "nunit"]));
        stages.Add(DetectStage("Integration Tests", PipelineStageType.IntegrationTest, workflowContent,
            ["integration", "e2e", "playwright"]));
        stages.Add(DetectStage("Security Scan", PipelineStageType.SecurityScan, workflowContent,
            ["security", "codeql", "snyk", "trivy", "gitleaks", "secret"]));
        stages.Add(DetectStage("Code Analysis", PipelineStageType.CodeAnalysis, workflowContent,
            ["sonar", "codacy", "analysis", "lint", "format"]));
        stages.Add(DetectStage("Deploy", PipelineStageType.Deploy, workflowContent,
            ["deploy", "publish", "release", "push"]));
        stages.Add(DetectStage("Smoke Test", PipelineStageType.SmokeTest, workflowContent,
            ["smoke", "health", "sanity"]));
        stages.Add(DetectStage("Performance", PipelineStageType.PerformanceTest, workflowContent,
            ["benchmark", "perf", "load"]));

        _logger.LogInformation("CI/CD scan found {PresentStages}/{TotalStages} pipeline stages present", stages.Count(s => s.Present), stages.Count);
        int presentStages = stages.Count(s => s.Present);
        int totalExpected = stages.Count;

        double pipelineCompleteness = totalExpected > 0 ? (double)presentStages / totalExpected : 0;
        bool hasWorkflows = !string.IsNullOrEmpty(workflowContent);

        // Detect environment configuration
        var envNames = DetectEnvironments(projectPath);
        int envCount = envNames.Count;
        _logger.LogInformation("Detected {EnvironmentCount} environments: {Environments}", envCount, string.Join(", ", envNames));
        double envParity = envCount >= 3 ? 1.0 : envCount >= 2 ? 0.7 : envCount >= 1 ? 0.4 : 0.0;

        // Detect rollback capability
        bool hasRollback = workflowContent.Contains("rollback", StringComparison.OrdinalIgnoreCase)
            || workflowContent.Contains("revert", StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(projectPath, "docker-compose.yml"));

        // ROSLYN## Build reliability: score based on CI maturity signals present in workflows
        bool hasCache = workflowContent.Contains("cache", StringComparison.OrdinalIgnoreCase);
        bool hasArtifacts = workflowContent.Contains("upload-artifact", StringComparison.OrdinalIgnoreCase);
        bool hasRetry = workflowContent.Contains("retry", StringComparison.OrdinalIgnoreCase)
            || workflowContent.Contains("continue-on-error", StringComparison.OrdinalIgnoreCase);
        bool hasMatrix = workflowContent.Contains("matrix", StringComparison.OrdinalIgnoreCase);
        double buildReliability = hasWorkflows
            ? 0.6 + (hasCache ? 0.1 : 0) + (hasArtifacts ? 0.1 : 0) + (hasRetry ? 0.1 : 0) + (hasMatrix ? 0.1 : 0)
            : 0.3;

        // ROSLYN## Build speed: score based on optimization features
        bool hasConcurrency = workflowContent.Contains("concurrency", StringComparison.OrdinalIgnoreCase);
        bool hasNoRestore = workflowContent.Contains("--no-restore", StringComparison.OrdinalIgnoreCase);
        double buildSpeed = 0.5 + (hasCache ? 0.15 : 0) + (hasConcurrency ? 0.1 : 0) + (hasNoRestore ? 0.1 : 0);

        // ROSLYN## Deploy frequency: score based on trigger breadth and deploy job presence
        bool hasPushTrigger = workflowContent.Contains("push:", StringComparison.OrdinalIgnoreCase);
        bool hasDeployJob = workflowContent.Contains("deploy", StringComparison.OrdinalIgnoreCase);
        bool hasRelease = workflowContent.Contains("release", StringComparison.OrdinalIgnoreCase)
            || workflowContent.Contains("tags:", StringComparison.OrdinalIgnoreCase);
        double deployFreq = hasWorkflows
            ? 0.4 + (hasPushTrigger ? 0.15 : 0) + (hasDeployJob ? 0.15 : 0) + (hasRelease ? 0.15 : 0)
            : 0.2;

        return new CiCdPipeline
        {
            BuildReliability = buildReliability,
            BuildSpeed = buildSpeed,
            DeployFrequency = deployFreq,
            PipelineCompleteness = pipelineCompleteness,
            EnvironmentParity = envParity,
            RollbackCapability = hasRollback ? (hasRelease ? 0.9 : 0.7) : 0.2,
            Build = new BuildProfile
            {
                AverageBuildTime = TimeSpan.Zero, // needs CI API
                MedianBuildTime = TimeSpan.Zero,
                SuccessRate = 0,
                BuildsPerWeek = 0,
                WarningTrend = 0,
                IncrementalBuildEnabled = true,
                CacheEnabled = workflowContent.Contains("cache", StringComparison.OrdinalIgnoreCase)
            },
            Deployment = new DeploymentProfile
            {
                DeploysPerWeek = 0,
                LeadTimeForChanges = TimeSpan.Zero,
                MeanTimeToRecover = TimeSpan.Zero,
                ChangeFailureRate = 0,
                Environments = envCount,
                EnvironmentNames = envNames,
                BlueGreenOrCanary = workflowContent.Contains("canary", StringComparison.OrdinalIgnoreCase)
                    || workflowContent.Contains("blue-green", StringComparison.OrdinalIgnoreCase),
                AutomatedRollback = hasRollback
            },
            Stages = stages
        };
    }

    private static async Task<string> ReadWorkflowFilesAsync(string path)
    {
        var content = "";

        // GitHub Actions
        var ghDir = Path.Combine(path, ".github", "workflows");
        if (Directory.Exists(ghDir))
        {
            foreach (var f in Directory.GetFiles(ghDir, "*.yml")
                .Concat(Directory.GetFiles(ghDir, "*.yaml")))
                content += await File.ReadAllTextAsync(f) + "\n";
        }

        // Azure DevOps
        var azurePipeline = Path.Combine(path, "azure-pipelines.yml");
        if (File.Exists(azurePipeline))
            content += await File.ReadAllTextAsync(azurePipeline) + "\n";

        return content;
    }

    /// <summary>
    /// Detect whether a pipeline stage is present based on workflow keyword matching.
    /// </summary>
    internal static PipelineStage DetectStage(string name, PipelineStageType type,
        string workflowContent, string[] keywords)
    {
        bool present = keywords.Any(k =>
            workflowContent.Contains(k, StringComparison.OrdinalIgnoreCase));

        return new PipelineStage
        {
            Name = name,
            Type = type,
            Present = present,
            Automated = present // if it's in CI, it's automated
        };
    }

    /// <summary>
    /// Detect deployment environments from appsettings files and Docker configuration.
    /// </summary>
    internal static List<string> DetectEnvironments(string path)
    {
        var envs = new List<string>();

        // Check appsettings files
        var settingsFiles = Directory.GetFiles(path, "appsettings.*.json", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/")
                     && !f.Replace('\\', '/').Contains("/bin/"));

        foreach (var f in settingsFiles)
        {
            var name = Path.GetFileNameWithoutExtension(f).Replace("appsettings.", "");
            if (!envs.Contains(name, StringComparer.OrdinalIgnoreCase) && name != "json")
                envs.Add(name);
        }

        // Check docker-compose for environment definitions
        if (File.Exists(Path.Combine(path, "docker-compose.yml")))
            if (!envs.Contains("Docker")) envs.Add("Docker");

        return envs;
    }
}
