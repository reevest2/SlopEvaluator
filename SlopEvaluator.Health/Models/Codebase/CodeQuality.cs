namespace SlopEvaluator.Health.Models;

/// <summary>
/// Static analysis, complexity, style, and code smell dimensions.
/// </summary>
public sealed record CodeQuality : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for maintainability index.</summary>
    public required double MaintainabilityIndex { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for cyclomatic complexity.</summary>
    public required double CyclomaticComplexity { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for low code duplication.</summary>
    public required double CodeDuplication { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for style consistency.</summary>
    public required double StyleConsistency { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for null safety practices.</summary>
    public required double NullSafety { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for error handling quality.</summary>
    public required double ErrorHandling { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for code readability.</summary>
    public required double Readability { get; init; }

    /// <summary>Detailed complexity breakdown by method and class.</summary>
    public required ComplexityProfile Complexity { get; init; }

    /// <summary>Analyzer warnings, errors, and style compliance details.</summary>
    public required StyleAnalysis Style { get; init; }

    /// <summary>Summary of detected code smells by category.</summary>
    public required CodeSmellSummary Smells { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (MaintainabilityIndex, 0.15),
        (CyclomaticComplexity, 0.15),
        (CodeDuplication, 0.15),
        (StyleConsistency, 0.10),
        (NullSafety, 0.15),
        (ErrorHandling, 0.15),
        (Readability, 0.15)
    );
}

/// <summary>
/// Detailed complexity metrics: method sizes, nesting depth, and hot spots.
/// </summary>
public sealed record ComplexityProfile
{
    /// <summary>Average cyclomatic complexity across all methods.</summary>
    public required double AverageCyclomaticComplexity { get; init; }

    /// <summary>Maximum cyclomatic complexity found in any single method.</summary>
    public required double MaxCyclomaticComplexity { get; init; }

    /// <summary>Number of methods exceeding the complexity threshold.</summary>
    public required int MethodsOverThreshold { get; init; }

    /// <summary>Average number of lines per method.</summary>
    public required double AverageLinesPerMethod { get; init; }

    /// <summary>Maximum lines found in any single method.</summary>
    public required double MaxLinesPerMethod { get; init; }

    /// <summary>Average nesting depth across all methods.</summary>
    public required double AverageNestingDepth { get; init; }

    /// <summary>Maximum nesting depth found in any single method.</summary>
    public required double MaxNestingDepth { get; init; }

    /// <summary>Number of classes identified as "god classes" (too many responsibilities).</summary>
    public required int GodClasses { get; init; }

    /// <summary>Number of methods considered too long.</summary>
    public required int LongMethods { get; init; }

    /// <summary>Most complex or problematic code locations.</summary>
    public required List<HotSpot> HotSpots { get; init; }
}

/// <summary>
/// A specific high-complexity location in the codebase.
/// </summary>
public sealed record HotSpot
{
    /// <summary>Path to the file containing the hot spot.</summary>
    public required string FilePath { get; init; }

    /// <summary>Name of the method flagged as a hot spot.</summary>
    public required string MethodName { get; init; }

    /// <summary>Cyclomatic complexity of the method.</summary>
    public required int CyclomaticComplexity { get; init; }

    /// <summary>Total line count of the method.</summary>
    public required int LineCount { get; init; }

    /// <summary>Maximum nesting depth in the method.</summary>
    public required int NestingDepth { get; init; }
}

/// <summary>
/// Analyzer and style rule compliance details.
/// </summary>
public sealed record StyleAnalysis
{
    /// <summary>Total number of analyzer warnings.</summary>
    public required int TotalWarnings { get; init; }

    /// <summary>Total number of analyzer errors.</summary>
    public required int TotalErrors { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for analyzer rule compliance.</summary>
    public required double AnalyzerCompliance { get; init; }

    /// <summary>Warning counts grouped by analyzer rule ID.</summary>
    public required Dictionary<string, int> WarningsByRule { get; init; }

    /// <summary>Whether an .editorconfig file is present.</summary>
    public required bool EditorConfigPresent { get; init; }

    /// <summary>Whether global suppressions are kept to a minimum.</summary>
    public required bool GlobalSuppressionsMinimal { get; init; }
}

/// <summary>
/// Aggregated code smell counts and top offenders.
/// </summary>
public sealed record CodeSmellSummary
{
    /// <summary>Total number of code smells detected.</summary>
    public required int TotalSmells { get; init; }

    /// <summary>Smell counts grouped by category.</summary>
    public required Dictionary<string, int> SmellsByCategory { get; init; }

    /// <summary>Highest-severity code smells found.</summary>
    public required List<CodeSmell> TopSmells { get; init; }
}

/// <summary>
/// A single code smell instance with location and severity.
/// </summary>
public sealed record CodeSmell
{
    /// <summary>Category of the code smell (e.g. "LongMethod", "GodClass").</summary>
    public required string Category { get; init; }

    /// <summary>Path to the file containing the smell.</summary>
    public required string FilePath { get; init; }

    /// <summary>Location within the file (e.g. class or method name).</summary>
    public required string Location { get; init; }

    /// <summary>Human-readable description of the smell.</summary>
    public required string Description { get; init; }

    /// <summary>Severity level (e.g. "Low", "Medium", "High").</summary>
    public required string Severity { get; init; }
}
