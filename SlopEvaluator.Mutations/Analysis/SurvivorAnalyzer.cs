using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;

namespace SlopEvaluator.Mutations.Analysis;

/// <summary>
/// Analyzes survivor patterns from mutation reports and produces
/// targeted recommendations for improving test coverage.
/// </summary>
public static class SurvivorAnalyzer
{
    /// <summary>
    /// Analyzes survivors in a mutation report and returns actionable recommendations.
    /// </summary>
    public static List<SurvivorRecommendation> Analyze(MutationReport report)
    {
        var survivors = report.Results
            .Where(r => r.Outcome == MutationOutcome.Survived)
            .ToList();

        if (survivors.Count == 0)
            return [];

        var recommendations = new List<SurvivorRecommendation>();

        // Group survivors by strategy to detect patterns
        var byStrategy = survivors.GroupBy(s => s.Strategy).ToList();

        foreach (var group in byStrategy)
        {
            var rec = AnalyzeStrategyGroup(group.Key, group.ToList());
            if (rec is not null)
                recommendations.Add(rec);
        }

        // Add cross-cutting recommendations
        if (survivors.Count >= 3)
        {
            recommendations.Add(new SurvivorRecommendation
            {
                Priority = "high",
                Category = "general",
                Title = "Multiple survivors detected",
                Description = $"{survivors.Count} mutations survived — consider reviewing assertion completeness across the test suite.",
                SuggestedTestPattern = "Add targeted assertions that verify the exact behavior each mutation changes."
            });
        }

        return recommendations.OrderByDescending(r => r.Priority == "high" ? 2 : r.Priority == "medium" ? 1 : 0).ToList();
    }

    private static SurvivorRecommendation? AnalyzeStrategyGroup(string strategy, List<MutationResultEntry> survivors)
    {
        return strategy switch
        {
            "linq-chain" => new SurvivorRecommendation
            {
                Priority = survivors.Count >= 2 ? "high" : "medium",
                Category = "linq",
                Title = $"LINQ filtering is undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that remove .Where() filters or swap .OrderBy() survived. " +
                    "This means tests don't verify that filtering/ordering logic produces correct results.",
                SuggestedTestPattern = "Add assertion-per-filter tests: for each .Where() clause, verify that " +
                    "items NOT matching the filter are excluded from results. " +
                    "For .OrderBy(), assert the order of returned items.",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            "boundary" => BuildBoundaryRecommendation(survivors),

            "logic" => new SurvivorRecommendation
            {
                Priority = "high",
                Category = "boolean",
                Title = $"Boolean logic is undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that swap && to ||, negate conditions, or invert boolean returns survived. " +
                    "Tests don't exercise both branches of compound conditions.",
                SuggestedTestPattern = "Test both branches of compound conditions: for `a && b`, " +
                    "test with (true,true), (true,false), (false,true), (false,false). " +
                    "For negation, verify both the true and false paths produce different results.",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            "return-value" => new SurvivorRecommendation
            {
                Priority = "medium",
                Category = "return-value",
                Title = $"Return values are undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that change return values to default/null/empty survived. " +
                    "Tests may only check for non-null without verifying actual values.",
                SuggestedTestPattern = "Assert exact return values, not just non-null. " +
                    "Use Assert.Equal() with expected values instead of Assert.NotNull().",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            "exception" => new SurvivorRecommendation
            {
                Priority = "high",
                Category = "exception",
                Title = $"Exception handling is undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that remove guard clauses or swallow exceptions survived. " +
                    "Tests don't verify that invalid inputs are rejected.",
                SuggestedTestPattern = "Add negative tests: pass invalid/null/out-of-range inputs and " +
                    "Assert.Throws<ExpectedException>() to verify guards are enforced.",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            "async" => new SurvivorRecommendation
            {
                Priority = "medium",
                Category = "async",
                Title = $"Async patterns are undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that remove await or fire-and-forget async calls survived.",
                SuggestedTestPattern = "Verify async operations complete and their side effects are observable. " +
                    "Use async test methods and await the results.",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            "string" => new SurvivorRecommendation
            {
                Priority = "medium",
                Category = "string",
                Title = $"String operations are undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that empty strings or swap case methods survived. " +
                    "Tests don't verify exact string content.",
                SuggestedTestPattern = "Assert exact string values with Assert.Equal(), not just Assert.NotNull(). " +
                    "Test case sensitivity and empty string edge cases.",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            "null-coalescing" => new SurvivorRecommendation
            {
                Priority = "high",
                Category = "null-coalescing",
                Title = $"Null fallback paths are undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that remove ?? fallback values survived. " +
                    "Tests don't verify behavior when inputs are null.",
                SuggestedTestPattern = "Test with null inputs and verify the fallback value is used. " +
                    "Test with non-null inputs and verify the fallback is NOT used.",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            "increment" => new SurvivorRecommendation
            {
                Priority = "medium",
                Category = "increment",
                Title = $"Counter/accumulator logic is undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that swap ++ with -- survived. " +
                    "Tests don't verify exact counts or accumulation direction.",
                SuggestedTestPattern = "Assert exact counter values after operations. " +
                    "Verify that incrementing produces value+1, not value-1.",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            "compound-assignment" => new SurvivorRecommendation
            {
                Priority = "medium",
                Category = "compound-assignment",
                Title = $"Accumulation logic is undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
                Description = "Mutations that swap += with -= survived. " +
                    "Tests don't verify running totals or accumulation results.",
                SuggestedTestPattern = "Assert exact accumulated values after multiple operations. " +
                    "Verify that += adds and -= subtracts.",
                AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList()
            },

            _ => survivors.Count >= 2 ? new SurvivorRecommendation
            {
                Priority = "medium",
                Category = strategy,
                Title = $"{strategy} mutations have survivors ({survivors.Count})",
                Description = $"Multiple {strategy} mutations survived — review test coverage for this pattern.",
                SuggestedTestPattern = "Add tests that specifically target the mutated behavior."
            } : null
        };
    }

    private static SurvivorRecommendation BuildBoundaryRecommendation(List<MutationResultEntry> survivors)
    {
        var boundaryTests = survivors
            .Select(s => ExtractBoundaryInfo(s))
            .Where(b => b is not null)
            .ToList();

        var specificAdvice = boundaryTests.Count > 0
            ? string.Join(" ", boundaryTests.Select(b =>
                $"Line {b!.LineNumber}: test with {string.Join(", ", b.TestValues)}."))
            : "Add exact-value boundary tests: test with the exact threshold value, one below, and one above.";

        return new SurvivorRecommendation
        {
            Priority = survivors.Count >= 2 ? "high" : "medium",
            Category = "boundary",
            Title = $"Boundary conditions are undertested ({survivors.Count} survivor{(survivors.Count > 1 ? "s" : "")})",
            Description = "Mutations that change < to <=, > to >=, or off-by-one survived. " +
                "Tests are not exercising exact boundary values.",
            SuggestedTestPattern = specificAdvice,
            AffectedLines = survivors.Select(s => s.LineNumberHint ?? 0).Where(l => l > 0).ToList(),
            BoundaryTests = boundaryTests.Count > 0 ? boundaryTests! : null
        };
    }

    /// <summary>
    /// Extracts boundary operator and threshold value from a survivor's original/mutated code.
    /// Returns specific test values (below, at, above the threshold).
    /// </summary>
    internal static BoundaryTestSuggestion? ExtractBoundaryInfo(MutationResultEntry survivor)
    {
        if (survivor.OriginalCode is null || survivor.MutatedCode is null)
            return null;

        var original = survivor.OriginalCode.Trim();
        var mutated = survivor.MutatedCode.Trim();

        // Find the operator that changed
        var operators = new[] { "<=", ">=", "!=", "==", "<", ">" };
        string? originalOp = null, mutatedOp = null;

        foreach (var op in operators)
        {
            if (original.Contains(op) && !mutated.Contains(op))
                originalOp = op;
            if (mutated.Contains(op) && !original.Contains(op))
                mutatedOp = op;
        }

        // Handle cases like < to <= (both contain <)
        if (originalOp is null || mutatedOp is null)
        {
            if (original.Contains("<=") && mutated.Contains("<") && !mutated.Contains("<="))
                { originalOp = "<="; mutatedOp = "<"; }
            else if (original.Contains("<") && !original.Contains("<=") && mutated.Contains("<="))
                { originalOp = "<"; mutatedOp = "<="; }
            else if (original.Contains(">=") && mutated.Contains(">") && !mutated.Contains(">="))
                { originalOp = ">="; mutatedOp = ">"; }
            else if (original.Contains(">") && !original.Contains(">=") && mutated.Contains(">="))
                { originalOp = ">"; mutatedOp = ">="; }
        }

        if (originalOp is null || mutatedOp is null)
            return null;

        // Extract the threshold value (right side of the operator)
        var opIndex = original.IndexOf(originalOp, StringComparison.Ordinal);
        var afterOp = original[(opIndex + originalOp.Length)..].Trim();

        // Try to extract a numeric literal
        var numMatch = System.Text.RegularExpressions.Regex.Match(afterOp, @"^-?\d+\.?\d*");
        var thresholdExpr = numMatch.Success ? numMatch.Value : afterOp.Split([' ', ')', ';', ',', '?'])[0];

        // Generate test values
        var testValues = new List<string>();
        if (double.TryParse(thresholdExpr, out var numVal))
        {
            var isInteger = numVal == Math.Floor(numVal);
            if (isInteger)
            {
                testValues.Add($"{(int)numVal - 1} (below)");
                testValues.Add($"{(int)numVal} (at boundary)");
                testValues.Add($"{(int)numVal + 1} (above)");
            }
            else
            {
                testValues.Add($"{numVal - 0.1} (below)");
                testValues.Add($"{numVal} (at boundary)");
                testValues.Add($"{numVal + 0.1} (above)");
            }
        }
        else
        {
            testValues.Add($"{thresholdExpr}-1 (below)");
            testValues.Add($"{thresholdExpr} (at boundary)");
            testValues.Add($"{thresholdExpr}+1 (above)");
        }

        return new BoundaryTestSuggestion
        {
            LineNumber = survivor.LineNumberHint ?? 0,
            OriginalOperator = originalOp,
            MutatedOperator = mutatedOp,
            ThresholdExpression = thresholdExpr,
            TestValues = testValues
        };
    }

    /// <summary>
    /// Formats recommendations for console output.
    /// </summary>
    public static string FormatRecommendations(List<SurvivorRecommendation> recommendations)
    {
        if (recommendations.Count == 0)
            return "  No survivors — all mutations killed!";

        var lines = new List<string> { "", "  ── Survivor Analysis ──────────────────────────────────────" };

        foreach (var rec in recommendations)
        {
            var icon = rec.Priority == "high" ? "🔴" : rec.Priority == "medium" ? "🟡" : "🟢";
            lines.Add($"  {icon} [{rec.Priority.ToUpperInvariant()}] {rec.Title}");
            lines.Add($"     {rec.Description}");
            lines.Add($"     Fix: {rec.SuggestedTestPattern}");

            if (rec.AffectedLines is { Count: > 0 })
                lines.Add($"     Lines: {string.Join(", ", rec.AffectedLines)}");

            if (rec.BoundaryTests is { Count: > 0 })
            {
                foreach (var bt in rec.BoundaryTests)
                    lines.Add($"     Line {bt.LineNumber}: {bt.OriginalOperator}→{bt.MutatedOperator} threshold={bt.ThresholdExpression} → test with {string.Join(", ", bt.TestValues)}");
            }

            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// A targeted recommendation based on survivor pattern analysis.
/// </summary>
public sealed class SurvivorRecommendation
{
    public required string Priority { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string SuggestedTestPattern { get; init; }
    public List<int>? AffectedLines { get; init; }

    /// <summary>Specific boundary test values extracted from survivor mutations.</summary>
    public List<BoundaryTestSuggestion>? BoundaryTests { get; init; }
}

/// <summary>
/// A specific boundary test value suggestion extracted from a surviving mutation.
/// </summary>
public sealed class BoundaryTestSuggestion
{
    public required int LineNumber { get; init; }
    public required string OriginalOperator { get; init; }
    public required string MutatedOperator { get; init; }
    public required string ThresholdExpression { get; init; }

    /// <summary>
    /// Suggested test values: [below, at, above] when threshold is numeric,
    /// or descriptive suggestions when it's a variable.
    /// </summary>
    public required List<string> TestValues { get; init; }
}
