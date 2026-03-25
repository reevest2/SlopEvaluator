namespace SlopEvaluator.Health.Models;

/// <summary>
/// Coverage, mutation testing, test quality, and token efficiency.
/// Bridges MutationHarness report data into the measurement model.
/// </summary>
public sealed record TestingStrategy : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for line coverage.</summary>
    public required double LineCoverage { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for branch coverage.</summary>
    public required double BranchCoverage { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for mutation testing kill rate.</summary>
    public required double MutationScore { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for edge case coverage.</summary>
    public required double EdgeCaseCoverage { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for overall test quality.</summary>
    public required double TestQualityScore { get; init; }

    /// <summary>Breakdown of test counts, run times, and frameworks.</summary>
    public required TestSuiteProfile Suite { get; init; }

    /// <summary>Test trustworthiness, precision, and diagnostic value.</summary>
    public required TestQualityProfile Quality { get; init; }

    /// <summary>Mutation testing results and survivor patterns.</summary>
    public required MutationTestingProfile MutationTesting { get; init; }

    /// <summary>Token spend and cost efficiency for AI-assisted testing.</summary>
    public required TokenEfficiencyProfile Efficiency { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (LineCoverage, 0.15),
        (BranchCoverage, 0.20),
        (MutationScore, 0.30),
        (EdgeCaseCoverage, 0.15),
        (TestQualityScore, 0.20)
    );
}

/// <summary>
/// Test suite composition: counts, timing, determinism, and framework info.
/// </summary>
public sealed record TestSuiteProfile
{
    /// <summary>Total number of tests in the suite.</summary>
    public required int TotalTests { get; init; }

    /// <summary>Number of unit tests.</summary>
    public required int UnitTests { get; init; }

    /// <summary>Number of integration tests.</summary>
    public required int IntegrationTests { get; init; }

    /// <summary>Number of end-to-end tests.</summary>
    public required int EndToEndTests { get; init; }

    /// <summary>Ratio of test lines to production code lines.</summary>
    public required double TestToCodeRatio { get; init; }

    /// <summary>Total wall-clock time to run the full suite.</summary>
    public required TimeSpan TotalRunTime { get; init; }

    /// <summary>Average duration of a single test.</summary>
    public required TimeSpan AverageTestDuration { get; init; }

    /// <summary>Number of tests identified as flaky (non-deterministic).</summary>
    public required int FlakyTests { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for test determinism.</summary>
    public required double Determinism { get; init; }

    /// <summary>Test framework in use (e.g. "xUnit", "NUnit").</summary>
    public required string Framework { get; init; }

    /// <summary>Test category labels defined in the suite.</summary>
    public required List<string> TestCategories { get; init; }
}

/// <summary>
/// Qualitative assessment of test trustworthiness and diagnostic value.
/// </summary>
public sealed record TestQualityProfile
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for test trustworthiness.</summary>
    public required double Trustworthiness { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for assertion precision.</summary>
    public required double Precision { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for test maintainability.</summary>
    public required double Maintainability { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for diagnostic value on failure.</summary>
    public required double DiagnosticValue { get; init; }

    /// <summary>Per-attribute quality scores keyed by attribute name.</summary>
    public required Dictionary<string, double> AttributeScores { get; init; }

    /// <summary>R-squared value from calibration regression.</summary>
    public required double RSquared { get; init; }

    /// <summary>Calibrated weights from regression analysis.</summary>
    public required double[] CalibratedWeights { get; init; }

    /// <summary>Most significant quality issues found in the test suite.</summary>
    public required List<string> TopIssues { get; init; }
}

/// <summary>
/// Mutation testing results: kill rate, survivor patterns, and throughput.
/// </summary>
public sealed record MutationTestingProfile
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for mutation kill rate.</summary>
    public required double MutationScore { get; init; }

    /// <summary>Total number of mutants generated.</summary>
    public required int TotalMutants { get; init; }

    /// <summary>Number of mutants killed by the test suite.</summary>
    public required int Killed { get; init; }

    /// <summary>Number of mutants that survived (not detected by tests).</summary>
    public required int Survived { get; init; }

    /// <summary>Number of mutants that caused compile errors.</summary>
    public required int CompileErrors { get; init; }

    /// <summary>Mutation scores broken down by mutation strategy.</summary>
    public required Dictionary<string, double> ScoreByStrategy { get; init; }

    /// <summary>Recurring patterns among surviving mutants.</summary>
    public required List<string> SurvivorPatterns { get; init; }

    /// <summary>Mutation testing throughput in mutants per minute.</summary>
    public required double MutationsPerMinute { get; init; }
}

/// <summary>
/// Token spend and cost efficiency metrics for AI-assisted test generation.
/// </summary>
public sealed record TokenEfficiencyProfile
{
    /// <summary>Total tokens consumed across all AI interactions.</summary>
    public required long TotalTokensSpent { get; init; }

    /// <summary>Estimated total cost in USD.</summary>
    public required double TotalCostEstimate { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for overall token efficiency.</summary>
    public required double OverallEfficiency { get; init; }

    /// <summary>Round number at which diminishing returns begin.</summary>
    public required int OptimalStopRound { get; init; }

    /// <summary>Lambda parameter for diminishing returns exponential decay.</summary>
    public required double DiminishingReturnsLambda { get; init; }

    /// <summary>Actionable recommendations for improving token efficiency.</summary>
    public required List<string> Recommendations { get; init; }
}
