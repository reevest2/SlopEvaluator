namespace SlopEvaluator.Health.Models;

/// <summary>
/// Root aggregate — everything measurable about a codebase.
/// </summary>
public sealed record Codebase : IScoreable
{
    /// <summary>Human-readable name of the codebase.</summary>
    public required string Name { get; init; }

    /// <summary>Remote repository URL (e.g. GitHub, Azure DevOps).</summary>
    public required string RepositoryUrl { get; init; }

    /// <summary>Date and time this measurement was taken.</summary>
    public required DateTime SnapshotDate { get; init; }

    /// <summary>.NET stack configuration for this codebase.</summary>
    public required DotNetStack Stack { get; init; }

    /// <summary>Architecture patterns and structural health.</summary>
    public required Architecture Architecture { get; init; }

    /// <summary>Solution layout and project organization.</summary>
    public required ProjectStructure Structure { get; init; }

    /// <summary>Static analysis, complexity, and code smell metrics.</summary>
    public required CodeQuality Quality { get; init; }

    /// <summary>Test coverage, mutation testing, and test quality.</summary>
    public required TestingStrategy Testing { get; init; }

    /// <summary>NuGet package health, vulnerabilities, and freshness.</summary>
    public required DependencyHealth Dependencies { get; init; }

    /// <summary>Security hygiene and OWASP coverage.</summary>
    public required SecurityPosture Security { get; init; }

    /// <summary>Logging, metrics, tracing, and alerting readiness.</summary>
    public required Observability Observability { get; init; }

    /// <summary>CI/CD build reliability and deployment health.</summary>
    public required CiCdPipeline Pipeline { get; init; }

    /// <summary>README, API docs, ADRs, and onboarding quality.</summary>
    public required Documentation Documentation { get; init; }

    /// <summary>Build times, tooling maturity, and inner loop speed.</summary>
    public required DeveloperExperience DevEx { get; init; }

    /// <summary>Startup time, memory, latency, and throughput.</summary>
    public required PerformanceProfile Performance { get; init; }

    /// <summary>Story clarity, acceptance criteria, and traceability.</summary>
    public required RequirementsQuality Requirements { get; init; }

    /// <summary>PR cycle time, review quality, and knowledge distribution.</summary>
    public required TeamProcessMetrics Process { get; init; }

    /// <summary>AI prompt interaction quality and efficiency.</summary>
    public required AIInteractionQuality AIQuality { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (Quality.Score, 0.15),
        (Testing.Score, 0.15),
        (Dependencies.Score, 0.08),
        (Security.Score, 0.10),
        (Observability.Score, 0.07),
        (Pipeline.Score, 0.08),
        (Documentation.Score, 0.05),
        (DevEx.Score, 0.07),
        (Performance.Score, 0.07),
        (Requirements.Score, 0.08),
        (Process.Score, 0.05),
        (AIQuality.Score, 0.05)
    );
}

/// <summary>
/// .NET stack configuration — what framework, language, and SDK features are in use.
/// </summary>
public sealed record DotNetStack
{
    /// <summary>Target framework moniker (e.g. "net8.0").</summary>
    public required string TargetFramework { get; init; }

    /// <summary>C# language version (e.g. "12.0").</summary>
    public required string LanguageVersion { get; init; }

    /// <summary>Whether nullable reference types are enabled.</summary>
    public required bool NullableEnabled { get; init; }

    /// <summary>Whether implicit usings are enabled.</summary>
    public required bool ImplicitUsings { get; init; }

    /// <summary>Whether the project is AOT-compatible.</summary>
    public required bool AotCompatible { get; init; }

    /// <summary>SDK type (e.g. "Microsoft.NET.Sdk.Web").</summary>
    public required string SdkType { get; init; }

    /// <summary>Global using directives configured for the project.</summary>
    public List<string> GlobalUsings { get; init; } = [];
}

/// <summary>
/// Architecture patterns, coupling, cohesion, and dependency direction.
/// </summary>
public sealed record Architecture : IScoreable
{
    /// <summary>Primary architecture pattern in use.</summary>
    public required ArchitecturePattern PrimaryPattern { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for layer separation.</summary>
    public required double LayerSeparation { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for dependency direction correctness.</summary>
    public required double DependencyDirection { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for low coupling.</summary>
    public required double CouplingScore { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for high cohesion.</summary>
    public required double CohesionScore { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for appropriate abstraction levels.</summary>
    public required double AbstractionLevel { get; init; }

    /// <summary>Number of circular dependency chains detected.</summary>
    public required int CircularDependencies { get; init; }

    /// <summary>Architecture and design patterns identified in the codebase.</summary>
    public required List<string> Patterns { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best), penalized by circular dependencies.</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (LayerSeparation, 0.20),
        (DependencyDirection, 0.25),
        (CouplingScore, 0.25),
        (CohesionScore, 0.20),
        (AbstractionLevel, 0.10)
    ) * (CircularDependencies == 0 ? 1.0 : Math.Max(0.5, 1.0 - CircularDependencies * 0.1));
}

/// <summary>
/// Known architecture patterns a codebase may follow.
/// </summary>
public enum ArchitecturePattern
{
    /// <summary>Clean Architecture (onion) pattern.</summary>
    CleanArchitecture,
    /// <summary>Vertical slice architecture.</summary>
    VerticalSlice,
    /// <summary>Traditional layered architecture.</summary>
    Layered,
    /// <summary>Hexagonal (ports and adapters) architecture.</summary>
    Hexagonal,
    /// <summary>Microservices architecture.</summary>
    Microservices,
    /// <summary>Monolithic architecture.</summary>
    Monolith,
    /// <summary>Modular monolith architecture.</summary>
    ModularMonolith,
    /// <summary>Command Query Responsibility Segregation pattern.</summary>
    Cqrs,
    /// <summary>Event-driven architecture.</summary>
    EventDriven
}
