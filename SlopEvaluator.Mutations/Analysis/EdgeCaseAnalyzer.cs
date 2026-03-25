using System.Text.RegularExpressions;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;

namespace SlopEvaluator.Mutations.Analysis;

/// <summary>
/// Analyzes C# source files to identify edge cases that should have test coverage.
/// Uses pattern matching on the source text — no Roslyn dependency needed for
/// this level of analysis. Feed the output to Claude for deeper analysis and
/// test generation.
/// </summary>
public sealed partial class EdgeCaseAnalyzer
{
    private readonly string _sourceFile;
    private readonly string? _target;
    private readonly string? _testFile;
    private readonly Action<string> _log;

    public EdgeCaseAnalyzer(
        string sourceFile,
        string? target = null,
        string? testFile = null,
        Action<string>? log = null)
    {
        _sourceFile = Path.GetFullPath(sourceFile);
        _target = target;
        _testFile = testFile;
        _log = log ?? Console.WriteLine;
    }

    public EdgeCaseReport Analyze()
    {
        if (!File.Exists(_sourceFile))
            throw new FileNotFoundException($"Source file not found: {_sourceFile}");

        var source = File.ReadAllText(_sourceFile);
        var lines = File.ReadAllLines(_sourceFile);

        string[]? testLines = null;
        string? testSource = null;
        if (_testFile is not null && File.Exists(_testFile))
        {
            testSource = File.ReadAllText(_testFile);
            testLines = File.ReadAllLines(_testFile);
        }

        _log("  Analyzing source for edge cases...");

        var edgeCases = new List<EdgeCase>();
        var paramBoundaries = new List<string>();
        var stateTransitions = new List<string>();
        int idCounter = 1;

        // ── Null parameters ────────────────────────────────────────
        foreach (var match in NullableParamRegex().Matches(source).Cast<Match>())
        {
            var paramName = match.Groups[2].Value;
            var paramType = match.Groups[1].Value;
            var lineNum = GetLineNumber(source, match.Index);

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "null-input",
                Description = $"Parameter '{paramName}' is nullable ({paramType}?) \u2014 test with null",
                RiskLevel = "high",
                LineNumber = lineNum,
                CodeContext = GetLineAt(lines, lineNum),
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_With{paramName}Null_ShouldHandleGracefully",
                CoveredByExistingTests = CheckTestCoverage(testSource, paramName, "null")
            });
        }

        // ── Guard clauses / throw statements ───────────────────────
        foreach (var match in GuardClauseRegex().Matches(source).Cast<Match>())
        {
            var lineNum = GetLineNumber(source, match.Index);
            var snippet = GetLineAt(lines, lineNum);

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "guard-clause",
                Description = $"Guard clause at line {lineNum} \u2014 test that exception is thrown for invalid input",
                RiskLevel = "high",
                LineNumber = lineNum,
                CodeContext = snippet,
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_WithInvalidInput_ShouldThrow",
                CoveredByExistingTests = CheckTestCoverage(testSource, "Throw", "Exception")
            });
        }

        // ── Boundary comparisons ───────────────────────────────────
        foreach (var match in BoundaryComparisonRegex().Matches(source).Cast<Match>())
        {
            var op = match.Groups[1].Value;
            var lineNum = GetLineNumber(source, match.Index);
            var snippet = GetLineAt(lines, lineNum);

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "boundary",
                Description = $"Boundary comparison '{op}' at line {lineNum} \u2014 test at, below, and above boundary",
                RiskLevel = "medium",
                LineNumber = lineNum,
                CodeContext = snippet,
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_AtBoundary_ShouldReturnCorrectResult",
                CoveredByExistingTests = null // hard to determine without deeper analysis
            });

            paramBoundaries.Add($"Line {lineNum}: {snippet?.Trim()} \u2014 test values at, just below, and just above the boundary");
        }

        // ── Empty collection handling ──────────────────────────────
        foreach (var match in EmptyCollectionRegex().Matches(source).Cast<Match>())
        {
            var method = match.Groups[1].Value;
            var lineNum = GetLineNumber(source, match.Index);

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "empty-collection",
                Description = $"Collection operation '.{method}()' at line {lineNum} \u2014 test with empty collection",
                RiskLevel = "medium",
                LineNumber = lineNum,
                CodeContext = GetLineAt(lines, lineNum),
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_WithEmptyCollection_ShouldHandleGracefully",
                CoveredByExistingTests = CheckTestCoverage(testSource, "empty", "Empty")
            });
        }

        // ── String operations ──────────────────────────────────────
        foreach (var match in StringOpRegex().Matches(source).Cast<Match>())
        {
            var lineNum = GetLineNumber(source, match.Index);

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "string-edge",
                Description = $"String operation at line {lineNum} \u2014 test with empty string, whitespace, and special characters",
                RiskLevel = "low",
                LineNumber = lineNum,
                CodeContext = GetLineAt(lines, lineNum),
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_WithEmptyString_ShouldHandleGracefully",
                CoveredByExistingTests = CheckTestCoverage(testSource, "empty", "\"\"", "whitespace")
            });
        }

        // ── Async/await patterns ───────────────────────────────────
        foreach (var match in AsyncPatternRegex().Matches(source).Cast<Match>())
        {
            var lineNum = GetLineNumber(source, match.Index);
            var snippet = GetLineAt(lines, lineNum);

            // Check for missing ConfigureAwait, fire-and-forget, etc.
            if (snippet?.Contains("ConfigureAwait") != true)
            {
                edgeCases.Add(new EdgeCase
                {
                    Id = $"EC{idCounter++:D3}",
                    Category = "async",
                    Description = $"Await at line {lineNum} \u2014 test cancellation, timeout, and exception propagation",
                    RiskLevel = "medium",
                    LineNumber = lineNum,
                    CodeContext = snippet,
                    SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_WhenCancelled_ShouldThrowOperationCancelled",
                    CoveredByExistingTests = CheckTestCoverage(testSource, "Cancel", "Timeout", "OperationCanceled")
                });
            }
        }

        // ── Try/catch blocks ───────────────────────────────────────
        foreach (var match in TryCatchRegex().Matches(source).Cast<Match>())
        {
            var exType = match.Groups[1].Value;
            var lineNum = GetLineNumber(source, match.Index);

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "error-handling",
                Description = $"Catch block for {exType} at line {lineNum} \u2014 test that exception path behaves correctly",
                RiskLevel = "high",
                LineNumber = lineNum,
                CodeContext = GetLineAt(lines, lineNum),
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_When{exType.Replace("Exception", "")}Occurs_ShouldHandleCorrectly",
                CoveredByExistingTests = CheckTestCoverage(testSource, exType)
            });
        }

        // ── Division operations ────────────────────────────────────
        foreach (var match in DivisionRegex().Matches(source).Cast<Match>())
        {
            var lineNum = GetLineNumber(source, match.Index);

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "divide-by-zero",
                Description = $"Division at line {lineNum} \u2014 test with zero denominator",
                RiskLevel = "high",
                LineNumber = lineNum,
                CodeContext = GetLineAt(lines, lineNum),
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_WithZeroDenominator_ShouldHandleGracefully",
                CoveredByExistingTests = CheckTestCoverage(testSource, "zero", "Zero", "DivideByZero")
            });
        }

        // ── Enum/switch patterns ───────────────────────────────────
        foreach (var match in SwitchRegex().Matches(source).Cast<Match>())
        {
            var lineNum = GetLineNumber(source, match.Index);
            var hasDefault = SwitchDefaultRegex().IsMatch(
                GetBlockAfter(source, match.Index, 500));

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "switch-coverage",
                Description = hasDefault
                    ? $"Switch at line {lineNum} \u2014 test all cases including default"
                    : $"Switch at line {lineNum} \u2014 NO DEFAULT CASE, test with unexpected value",
                RiskLevel = hasDefault ? "medium" : "high",
                LineNumber = lineNum,
                CodeContext = GetLineAt(lines, lineNum),
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_WithUnexpectedValue_ShouldHandleCorrectly",
                CoveredByExistingTests = null
            });
        }

        // ── LINQ FirstOrDefault / SingleOrDefault ──────────────────
        foreach (var match in DefaultReturnRegex().Matches(source).Cast<Match>())
        {
            var method = match.Groups[1].Value;
            var lineNum = GetLineNumber(source, match.Index);

            edgeCases.Add(new EdgeCase
            {
                Id = $"EC{idCounter++:D3}",
                Category = "default-return",
                Description = $"'{method}' at line {lineNum} \u2014 test when no match exists (returns null/default)",
                RiskLevel = "medium",
                LineNumber = lineNum,
                CodeContext = GetLineAt(lines, lineNum),
                SuggestedTestName = $"{GetMethodContext(lines, lineNum)}_WhenNoMatch_ShouldReturnDefault",
                CoveredByExistingTests = CheckTestCoverage(testSource, "null", "default", "NotFound")
            });
        }

        // ── Identify state transitions from property sets ──────────
        foreach (var match in StateChangeRegex().Matches(source).Cast<Match>())
        {
            var prop = match.Groups[1].Value;
            var lineNum = GetLineNumber(source, match.Index);
            stateTransitions.Add($"Line {lineNum}: {prop} is modified \u2014 test state before and after");
        }

        // Build category summary
        var categorySummary = edgeCases
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        _log($"  Found {edgeCases.Count} edge cases across {categorySummary.Count} categories");

        return new EdgeCaseReport
        {
            SourceFile = _sourceFile,
            Target = _target,
            GeneratedAt = DateTime.UtcNow,
            EdgeCases = edgeCases,
            ParameterBoundaries = paramBoundaries,
            StateTransitions = stateTransitions,
            CategorySummary = categorySummary
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static int GetLineNumber(string source, int charIndex)
    {
        int line = 1;
        for (int i = 0; i < charIndex && i < source.Length; i++)
        {
            if (source[i] == '\n') line++;
        }
        return line;
    }

    private static string? GetLineAt(string[] lines, int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > lines.Length) return null;
        return lines[lineNumber - 1].Trim();
    }

    private static string GetBlockAfter(string source, int startIndex, int maxLength)
    {
        var end = Math.Min(startIndex + maxLength, source.Length);
        return source[startIndex..end];
    }

    private static string GetMethodContext(string[] lines, int lineNumber)
    {
        // Walk backwards from the line to find the containing method name
        for (int i = Math.Min(lineNumber - 1, lines.Length - 1); i >= 0; i--)
        {
            var match = MethodNameRegex().Match(lines[i]);
            if (match.Success)
                return match.Groups[1].Value;
        }
        return "Method";
    }

    /// <summary>
    /// Basic heuristic: check if test source contains keywords that suggest coverage.
    /// Returns null if no test file is available.
    /// </summary>
    private static bool? CheckTestCoverage(string? testSource, params string[] keywords)
    {
        if (testSource is null) return null;

        // If ANY keyword appears in the test source, optimistically assume covered
        // This is a rough heuristic — the mutation test is the real verification
        return keywords.Any(kw =>
            testSource.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    // ── Regex patterns ──────────────────────────────────────────────

    [GeneratedRegex(@"(\w+)\?\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex NullableParamRegex();

    [GeneratedRegex(@"(?:throw\s+new\s+\w+|ArgumentNullException\.ThrowIfNull|ArgumentException\.ThrowIf)", RegexOptions.Compiled)]
    private static partial Regex GuardClauseRegex();

    [GeneratedRegex(@"[^!=<>](<=|>=|<|>|==|!=)\s*\d", RegexOptions.Compiled)]
    private static partial Regex BoundaryComparisonRegex();

    [GeneratedRegex(@"\.(First|Last|Single|Min|Max|Average|Sum|Count|Any|All|Aggregate)\s*\(", RegexOptions.Compiled)]
    private static partial Regex EmptyCollectionRegex();

    [GeneratedRegex(@"(?:string\.IsNullOrEmpty|string\.IsNullOrWhiteSpace|\.Trim\(|\.Split\(|\.Substring\(|\.Replace\()", RegexOptions.Compiled)]
    private static partial Regex StringOpRegex();

    [GeneratedRegex(@"await\s+", RegexOptions.Compiled)]
    private static partial Regex AsyncPatternRegex();

    [GeneratedRegex(@"catch\s*\(\s*(\w+(?:Exception)?)", RegexOptions.Compiled)]
    private static partial Regex TryCatchRegex();

    [GeneratedRegex(@"[\w\]\)]\s*/\s*[\w\[\(]", RegexOptions.Compiled)]
    private static partial Regex DivisionRegex();

    [GeneratedRegex(@"switch\s*[\(\{]", RegexOptions.Compiled)]
    private static partial Regex SwitchRegex();

    [GeneratedRegex(@"default\s*[:\{]|_\s*=>", RegexOptions.Compiled)]
    private static partial Regex SwitchDefaultRegex();

    [GeneratedRegex(@"\.(FirstOrDefault|SingleOrDefault|LastOrDefault|Find)\s*\(", RegexOptions.Compiled)]
    private static partial Regex DefaultReturnRegex();

    [GeneratedRegex(@"\.(\w+)\s*=\s*[^=]", RegexOptions.Compiled)]
    private static partial Regex StateChangeRegex();

    [GeneratedRegex(@"(?:public|private|protected|internal)\s+\S+\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodNameRegex();
}
