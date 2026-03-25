using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
namespace SlopEvaluator.Mutations.Fix;

/// <summary>
/// A generated test designed to kill a specific surviving mutant.
/// </summary>
public sealed class KillingTest
{
    public required string SurvivorId { get; init; }
    public required string Strategy { get; init; }
    public required string TestName { get; init; }
    public required string TestCode { get; init; }
    public required string Description { get; init; }
    public required string OriginalCode { get; init; }
    public required string MutatedCode { get; init; }
    public int? LineNumber { get; init; }
    public bool Verified { get; set; }
    public string? VerificationError { get; set; }
}

/// <summary>
/// Report of what the fix harness generated.
/// </summary>
public sealed class FixReport
{
    public required string ReportPath { get; init; }
    public required string SourceFile { get; init; }
    public required string OutputFile { get; init; }
    public required int SurvivorsFound { get; init; }
    public required int TestsGenerated { get; init; }
    public required int TestsVerified { get; init; }
    public required List<KillingTest> Tests { get; init; }
}

/// <summary>
/// Survivor from a mutation report — the input to the fix harness.
/// </summary>
public sealed class Survivor
{
    public string Id { get; init; } = "";
    public string Strategy { get; init; } = "";
    public string Description { get; init; } = "";
    public string RiskLevel { get; init; } = "";
    public string? OriginalCode { get; init; }
    public string? MutatedCode { get; init; }
    public int? LineNumberHint { get; init; }
}

/// <summary>
/// Minimal mutation report structure — just enough to read survivors.
/// </summary>
public sealed class MutationReportInput
{
    public string SourceFile { get; init; } = "";
    public string Target { get; init; } = "";
    public List<MutationResultInput> Results { get; init; } = [];
}

public sealed class MutationResultInput
{
    public string Id { get; init; } = "";
    public string Strategy { get; init; } = "";
    public string Description { get; init; } = "";
    public string RiskLevel { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string? OriginalCode { get; init; }
    public string? MutatedCode { get; init; }
    public int? LineNumberHint { get; init; }
}
