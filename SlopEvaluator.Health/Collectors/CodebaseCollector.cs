using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;
using SlopEvaluator.Health.Storage;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Orchestrates all collectors to produce a full Codebase measurement.
/// </summary>
public class CodebaseCollector
{
    private readonly InteractionStore? _interactionStore;
    private readonly ILogger<CodebaseCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodebaseCollector"/> class.
    /// </summary>
    /// <param name="interactionStore">Optional interaction store for AI quality metrics.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public CodebaseCollector(InteractionStore? interactionStore = null, ILogger<CodebaseCollector>? logger = null)
    {
        _interactionStore = interactionStore;
        _logger = logger ?? NullLogger<CodebaseCollector>.Instance;
    }

    /// <summary>
    /// Run all collectors against a project path and produce a complete Codebase score.
    /// </summary>
    public async Task<Models.Codebase> CollectAsync(string projectPath, string name, string? repoUrl = null, string? domain = null)
    {
        _logger.LogInformation("Starting codebase collection for {Name} at {Path}", name, projectPath);

        // Run all collectors
        var codeQuality = await new CodeQualityCollector().CollectAsync(projectPath);
        _logger.LogInformation("CodeQuality collected: {Score:F3}", codeQuality.Score);

        var structure = await new ProjectStructureCollector().CollectAsync(projectPath);
        _logger.LogInformation("ProjectStructure collected: {Score:F3}", structure.Score);

        var deps = await new DependencyHealthCollector().CollectAsync(projectPath);
        _logger.LogInformation("DependencyHealth collected: {Score:F3}", deps.Score);

        var docs = await new DocumentationCollector().CollectAsync(projectPath);
        var security = await new SecurityPostureCollector().CollectAsync(projectPath);
        var observability = await new ObservabilityCollector().CollectAsync(projectPath);
        var pipeline = await new CiCdPipelineCollector().CollectAsync(projectPath);
        var performance = await new PerformanceProfileCollector().CollectAsync(projectPath);

        // Testing strategy from MutationHarness reports
        var testing = await new TestingStrategyCollector().CollectAsync(projectPath);

        // DevEx involves timed builds — run it
        var devex = await new DeveloperExperienceCollector().CollectAsync(projectPath);

        // Team process from git
        var process = await new TeamProcessCollector().CollectAsync(projectPath);

        // AI quality from interaction store
        var aiQuality = _interactionStore is not null && domain is not null
            ? await new AIInteractionCollector(_interactionStore).CollectAsync(domain)
            : EmptyAIQuality();

        // Requirements from GitHub issues
        var requirements = await new RequirementsCollector().CollectAsync(projectPath);

        // Detect stack info
        var stack = await DetectStackAsync(projectPath);

        // Architecture — inferred from structure
        var architecture = InferArchitecture(structure, codeQuality);

        // Wire dependency security into security posture
        security = security with { DependencySecurity = deps.VulnerabilityFreedom };

        var result = new Models.Codebase
        {
            Name = name,
            RepositoryUrl = repoUrl ?? "",
            SnapshotDate = DateTime.UtcNow,
            Stack = stack,
            Architecture = architecture,
            Structure = structure,
            Quality = codeQuality,
            Testing = testing,
            Dependencies = deps,
            Security = security,
            Observability = observability,
            Pipeline = pipeline,
            Documentation = docs,
            DevEx = devex,
            Performance = performance,
            Requirements = requirements,
            Process = process,
            AIQuality = aiQuality
        };

        _logger.LogInformation("Collection complete for {Name}. Overall score: {Score:F3}", name, result.Score);
        return result;
    }

    private static async Task<DotNetStack> DetectStackAsync(string path)
    {
        string targetFramework = "unknown";
        string languageVersion = "unknown";
        bool nullable = false;
        bool implicitUsings = false;
        bool aot = false;
        string sdkType = "classlib";

        var csprojs = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/")
                     && !f.Replace('\\', '/').Contains("/bin/")
                     && !f.Replace('\\', '/').Contains("/benchmarks/"))
            .Take(5);

        foreach (var csproj in csprojs)
        {
            var content = await File.ReadAllTextAsync(csproj);

            if (content.Contains("<TargetFramework>"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(content, @"<TargetFramework>([^<]+)");
                if (match.Success) targetFramework = match.Groups[1].Value;
            }
            if (content.Contains("<Nullable>enable")) nullable = true;
            if (content.Contains("<ImplicitUsings>enable")) implicitUsings = true;
            if (content.Contains("PublishAot")) aot = true;
            if (content.Contains("Microsoft.NET.Sdk.Web")) sdkType = "web";
            else if (content.Contains("Microsoft.NET.Sdk.BlazorWebAssembly")) sdkType = "blazorwasm";
            else if (content.Contains("<OutputType>Exe")) sdkType = "console";
        }

        return new DotNetStack
        {
            TargetFramework = targetFramework,
            LanguageVersion = languageVersion,
            NullableEnabled = nullable,
            ImplicitUsings = implicitUsings,
            AotCompatible = aot,
            SdkType = sdkType
        };
    }

    private static Architecture InferArchitecture(ProjectStructure structure, CodeQuality quality)
    {
        var projects = structure.Projects;
        var patterns = new List<string>();

        // Detect patterns from project names
        if (projects.Any(p => p.Name.Contains("MediatR") || p.Name.Contains("Mediator")))
            patterns.Add("CQRS/MediatR");
        if (projects.Any(p => p.Name.Contains("DataAccess") || p.Name.Contains("Infrastructure")))
            patterns.Add("Layered");
        if (projects.Any(p => p.Name.Contains(".Core") || p.Name.Contains(".Domain")))
            patterns.Add("CleanArchitecture");

        // Check package references for patterns
        foreach (var p in projects)
        {
            if (p.PackageReferences.Any(r => r.Contains("MediatR"))) patterns.Add("MediatR");
            if (p.PackageReferences.Any(r => r.Contains("FluentValidation"))) patterns.Add("FluentValidation");
        }

        patterns = patterns.Distinct().ToList();
        var primary = patterns.Count > 0 ? ArchitecturePattern.Layered : ArchitecturePattern.Monolith;

        // Coupling: more projects with fewer cross-references = less coupled
        double avgRefs = projects.Count > 0
            ? projects.Average(p => p.ProjectReferences.Count)
            : 0;
        double coupling = avgRefs <= 2 ? 1.0 : avgRefs <= 5 ? 0.7 : 0.4;

        return new Architecture
        {
            PrimaryPattern = primary,
            LayerSeparation = structure.ProjectCount >= 3 ? 0.8 : 0.4,
            DependencyDirection = 0.7,
            CouplingScore = coupling,
            CohesionScore = 0.7,
            AbstractionLevel = 0.6,
            CircularDependencies = 0,
            Patterns = patterns
        };
    }

    private static TestingStrategy EmptyTesting() => new()
    {
        LineCoverage = 0, BranchCoverage = 0, MutationScore = 0,
        EdgeCaseCoverage = 0, TestQualityScore = 0,
        Suite = new TestSuiteProfile
        {
            TotalTests = 0, UnitTests = 0, IntegrationTests = 0, EndToEndTests = 0,
            TestToCodeRatio = 0, TotalRunTime = TimeSpan.Zero,
            AverageTestDuration = TimeSpan.Zero, FlakyTests = 0, Determinism = 1.0,
            Framework = "unknown", TestCategories = []
        },
        Quality = new TestQualityProfile
        {
            Trustworthiness = 0, Precision = 0, Maintainability = 0, DiagnosticValue = 0,
            AttributeScores = [], RSquared = 0, CalibratedWeights = [], TopIssues = []
        },
        MutationTesting = new MutationTestingProfile
        {
            MutationScore = 0, TotalMutants = 0, Killed = 0, Survived = 0,
            CompileErrors = 0, ScoreByStrategy = [], SurvivorPatterns = [], MutationsPerMinute = 0
        },
        Efficiency = new TokenEfficiencyProfile
        {
            TotalTokensSpent = 0, TotalCostEstimate = 0, OverallEfficiency = 0,
            OptimalStopRound = 0, DiminishingReturnsLambda = 0, Recommendations = []
        }
    };

    private static RequirementsQuality EmptyRequirements() => new()
    {
        Clarity = 0.5, Completeness = 0.5, Testability = 0.5,
        Atomicity = 0.5, AcceptanceCriteriaQuality = 0.5, TraceabilityToCode = 0.5,
        Stories = []
    };

    private static AIInteractionQuality EmptyAIQuality() => new()
    {
        AverageEffectiveScore = 0, AverageEfficiency = 0, FirstPassSuccessRate = 0,
        ContextLeverage = 0.5, DomainSignalEffectiveness = 0.5, ImprovementTrend = 0.5,
        Usage = new AIUsageProfile
        {
            TotalInteractions = 0, AverageIterations = 0,
            InteractionsByCategory = [], MostEffectiveCategory = "N/A",
            LeastEffectiveCategory = "N/A", TokenEfficiencyOverall = 0, TopCorrections = []
        },
        ScoreByCategory = [], InputLeverage = [], ScoreTrend = []
    };
}
