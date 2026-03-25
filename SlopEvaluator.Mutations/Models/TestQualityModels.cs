using SlopEvaluator.Mutations.Analysis;
namespace SlopEvaluator.Mutations.Models;

public enum TestQualityGrade { F, D, C, B, A }

public record TestQualityReport
{
    public required string TestFile { get; init; }
    public int TestsAnalyzed { get; init; }
    public double TotalScore { get; init; }
    public double MaxScore { get; init; } = 48;
    public TestQualityGrade Grade { get; init; }
    public required Dictionary<string, PillarScore> Pillars { get; init; }
    public required List<TestIssue> Issues { get; init; }
    public required List<TestMethodScore> TestScores { get; init; }

    public double Percentage => MaxScore > 0 ? TotalScore / MaxScore * 100 : 0;

    public static TestQualityGrade ComputeGrade(double score) => score switch
    {
        >= 40 => TestQualityGrade.A,
        >= 32 => TestQualityGrade.B,
        >= 24 => TestQualityGrade.C,
        >= 16 => TestQualityGrade.D,
        _ => TestQualityGrade.F
    };
}

public record PillarScore
{
    public required string Name { get; init; }
    public double Score { get; init; }
    public double MaxScore { get; init; } = 12;
    public required Dictionary<string, double> Attributes { get; init; }
}

public record TestMethodScore
{
    public required string TestName { get; init; }
    public int LineNumber { get; init; }
    public required double[] AttributeScores { get; init; } // 16 scores, 0-3 each
    public double Total => AttributeScores.Sum();
}

public record TestIssue
{
    public required string TestName { get; init; }
    public required string Attribute { get; init; }
    public string? Smell { get; init; }
    public required string Description { get; init; }
    public int LineNumber { get; init; }
    public int Score { get; init; }
}

// ── Weight calibration ──────────────────────────────────────

public record WeightProfile
{
    public int Version { get; init; } = 1;
    public string? TrainedOn { get; init; }
    public int DataPoints { get; init; }
    public double RSquared { get; init; }
    public required double[] Weights { get; init; } // 16 weights
    public double Bias { get; init; }
    public List<string> Insights { get; init; } = [];

    public static WeightProfile Default => new()
    {
        Weights = Enumerable.Repeat(1.0, 16).ToArray(),
        Bias = 0,
        DataPoints = 0,
        RSquared = 0,
        TrainedOn = null
    };
}

public record CalibrationDataPoint
{
    public required string TestFile { get; init; }
    public required double[] AttributeScores { get; init; } // 16 scores
    public double MutationScore { get; init; }
}

public record CalibrationResult
{
    public required WeightProfile Profile { get; init; }
    public required List<string> Insights { get; init; }
    public double MeanAbsoluteError { get; init; }
}

// ── Attribute index constants ────────────────────────────────

public static class QualityAttributes
{
    // Trustworthiness
    public const int Determinism = 0;
    public const int Isolation = 1;
    public const int CorrectOracle = 2;
    public const int SignalIntegrity = 3;

    // Precision
    public const int SingleConcern = 4;
    public const int BehavioralFocus = 5;
    public const int BoundaryCoverage = 6;
    public const int MeaningfulScope = 7;

    // Maintainability
    public const int Readability = 8;
    public const int RefactorResistance = 9;
    public const int SetupSimplicity = 10;
    public const int Independence = 11;

    // Diagnostic Value
    public const int FailureLocalization = 12;
    public const int ExecutionSpeed = 13;
    public const int RegressionDetection = 14;
    public const int ErrorMessageQuality = 15;

    public const int Count = 16;

    public static readonly string[] Names =
    [
        "determinism", "isolation", "correct_oracle", "signal_integrity",
        "single_concern", "behavioral_focus", "boundary_coverage", "meaningful_scope",
        "readability", "refactor_resistance", "setup_simplicity", "independence",
        "failure_localization", "execution_speed", "regression_detection", "error_message_quality"
    ];

    public static readonly string[] PillarNames = ["Trustworthiness", "Precision", "Maintainability", "Diagnostic Value"];

    public static int PillarFor(int attrIndex) => attrIndex / 4;
}
