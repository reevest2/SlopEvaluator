using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Models;

/// <summary>
/// Top-level config loaded from mutations.json or passed via CLI args.
/// </summary>
public sealed record HarnessConfig
{
    /// <summary>Path to the source file to mutate.</summary>
    public required string SourceFile { get; init; }

    /// <summary>Path to the solution or test project (for dotnet build/test).</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Full dotnet test command (e.g., "dotnet test ./Tests/Tests.csproj").</summary>
    public required string TestCommand { get; init; }

    /// <summary>Optional: specific method or class to target (fully qualified).</summary>
    public string? Target { get; init; }

    /// <summary>Mutations to apply. Can be auto-generated or loaded from file.</summary>
    public List<MutationSpec> Mutations { get; init; } = [];

    /// <summary>Optional: recommended test file to validate against surviving mutants.</summary>
    public string? RecommendedTestFile { get; init; }

    /// <summary>Timeout per test run in seconds.</summary>
    public int TestTimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Multiplier for baseline duration to detect timeout mutations.
    /// A mutation taking longer than baseline * this value is classified as timeout.
    /// Default: 3.0. Lower values catch slow mutations more aggressively.
    /// </summary>
    public double TimeoutMultiplier { get; init; } = 3.0;

    /// <summary>
    /// Absolute minimum timeout floor in seconds. Prevents false timeouts
    /// when baseline is very fast. Default: 5 seconds.
    /// </summary>
    public int AbsoluteTimeoutFloorSeconds { get; init; } = 5;

    /// <summary>Where to write the report.</summary>
    public string ReportPath { get; init; } = "mutation-report.json";

    /// <summary>Include mutations from other config files (merged into this config).</summary>
    public List<string>? Include { get; init; }

    /// <summary>Inherit sourceFile/testCommand/projectPath from a base config.</summary>
    public string? Extends { get; init; }
}

/// <summary>
/// A single mutation definition — what to find and what to replace it with.
/// </summary>
public sealed class MutationSpec
{
    public required string Id { get; init; }
    public required string Strategy { get; init; }
    public required string Description { get; init; }
    public required string OriginalCode { get; init; }
    public required string MutatedCode { get; init; }
    public string RiskLevel { get; init; } = "medium";

    /// <summary>
    /// Optional: line number hint to disambiguate if OriginalCode appears multiple times.
    /// </summary>
    public int? LineNumberHint { get; init; }

    /// <summary>
    /// Optional: fully-qualified method name to target (e.g., "MyClass.DoWork").
    /// Used by structural mutations to locate the correct method via Roslyn AST.
    /// </summary>
    public string? TargetMethod { get; init; }

    /// <summary>
    /// Optional: 0-based index of the target node within the method.
    /// For remove-statement, this is the statement index.
    /// For swap-statements, this is the first statement index (swaps with index+1).
    /// </summary>
    public int? NodeIndex { get; init; }

    /// <summary>
    /// Optional: strategy-specific parameters as a dictionary.
    /// </summary>
    public Dictionary<string, string>? Parameters { get; init; }
}

public enum MutationOutcome
{
    Killed,
    Survived,
    CompileError,
    Timeout,
    Error
}

/// <summary>
/// Result of executing a single mutation against the test suite.
/// </summary>
public sealed class MutationResult
{
    public required string MutationId { get; init; }
    public required MutationOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? ErrorOutput { get; init; }
    public int TestsRun { get; init; }
    public int TestsFailed { get; init; }
    public string? FailedTestNames { get; init; }
}

/// <summary>
/// Full report from a harness run.
/// </summary>
public sealed class MutationReport
{
    public required string SourceFile { get; init; }
    public required string Target { get; init; }
    public required DateTime RunDate { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required TimeSpan BaselineDuration { get; init; }
    public required int BaselineTestCount { get; init; }
    public required List<MutationResultEntry> Results { get; init; }
    public RecommendedTestResults? RecommendedTestValidation { get; init; }

    /// <summary>
    /// Survivor pattern analysis recommendations from SurvivorAnalyzer.
    /// Populated after report generation for JSON/HTML persistence.
    /// </summary>
    public List<SurvivorRecommendation>? Recommendations { get; set; }

    public int Killed => Results.Count(r => r.Outcome == MutationOutcome.Killed);
    public int Survived => Results.Count(r => r.Outcome == MutationOutcome.Survived);
    public int CompileErrors => Results.Count(r => r.Outcome == MutationOutcome.CompileError);
    public int Timeouts => Results.Count(r => r.Outcome == MutationOutcome.Timeout);
    public int TotalValid => Killed + Survived;
    public double MutationScore => TotalValid == 0 ? 0 : (double)Killed / TotalValid * 100;
}

/// <summary>
/// A single entry in the report, combining the spec with its result.
/// </summary>
public sealed class MutationResultEntry
{
    public required string Id { get; init; }
    public required string Strategy { get; init; }
    public required string Description { get; init; }
    public required string RiskLevel { get; init; }
    public required MutationOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? OriginalCode { get; init; }
    public string? MutatedCode { get; init; }
    public int? LineNumberHint { get; init; }
    public string? ErrorOutput { get; init; }
    public int TestsRun { get; init; }
    public int TestsFailed { get; init; }
    public string? FailedTestNames { get; init; }
}

/// <summary>
/// Results from validating AI-recommended tests against surviving mutants.
/// </summary>
public sealed class RecommendedTestResults
{
    public required string TestFile { get; init; }
    public required bool PassesOnOriginal { get; init; }
    public required List<RecommendedTestVsMutant> MutantResults { get; init; }
    public int MutantsNowKilled => MutantResults.Count(r => r.NowKilled);
    public int MutantsStillSurviving => MutantResults.Count(r => !r.NowKilled);
}

public sealed class RecommendedTestVsMutant
{
    public required string MutationId { get; init; }
    public required bool NowKilled { get; init; }
    public string? Details { get; init; }
}
