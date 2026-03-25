using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;

namespace SlopEvaluator.Mutations.Analysis;

/// <summary>
/// Analyzes test quality using Roslyn AST — 4 pillars, 16 attributes, 48-point scale.
/// Based on Microsoft .NET unit testing best practices and the Unit Test Quality Model.
/// </summary>
public class TestQualityAnalyzer
{
    private readonly string _testFile;
    private readonly Action<string> _log;
    private readonly WeightProfile? _weights;

    /// <summary>
    /// Initializes the analyzer for the given test file.
    /// </summary>
    /// <param name="testFile">Path to the C# test file to analyze.</param>
    /// <param name="weights">Optional calibrated weight profile for weighted scoring.</param>
    /// <param name="log">Optional logging callback.</param>
    public TestQualityAnalyzer(string testFile, WeightProfile? weights = null, Action<string>? log = null)
    {
        _testFile = testFile;
        _weights = weights;
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// Parses the test file with Roslyn, scores each test method across 16 attributes,
    /// and returns an aggregate quality report.
    /// </summary>
    /// <returns>A <see cref="TestQualityReport"/> with per-method scores, pillar totals, and detected issues.</returns>
    public TestQualityReport Analyze()
    {
        if (!File.Exists(_testFile))
            throw new FileNotFoundException($"Test file not found: {_testFile}", _testFile);

        var source = File.ReadAllText(_testFile);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var finder = new TestMethodFinder();
        finder.Visit(root);

        if (finder.TestMethods.Count == 0)
        {
            return new TestQualityReport
            {
                TestFile = _testFile,
                TestsAnalyzed = 0,
                TotalScore = 0,
                Grade = TestQualityGrade.F,
                Pillars = new Dictionary<string, PillarScore>(),
                Issues = [],
                TestScores = []
            };
        }

        var issues = new List<TestIssue>();
        var testScores = new List<TestMethodScore>();

        foreach (var method in finder.TestMethods)
        {
            var scores = ScoreTestMethod(method, root, issues);
            testScores.Add(scores);
        }

        // Compute averages per attribute
        var avgScores = new double[QualityAttributes.Count];
        for (int a = 0; a < QualityAttributes.Count; a++)
        {
            avgScores[a] = testScores.Average(t => t.AttributeScores[a]);
        }

        // Build pillar scores
        var pillars = new Dictionary<string, PillarScore>();
        for (int p = 0; p < 4; p++)
        {
            var attrs = new Dictionary<string, double>();
            for (int a = p * 4; a < p * 4 + 4; a++)
            {
                attrs[QualityAttributes.Names[a]] = Math.Round(avgScores[a], 1);
            }
            pillars[QualityAttributes.PillarNames[p]] = new PillarScore
            {
                Name = QualityAttributes.PillarNames[p],
                Score = Math.Round(attrs.Values.Sum(), 1),
                Attributes = attrs
            };
        }

        var total = _weights is not null
            ? ComputeWeightedScore(avgScores, _weights)
            : Math.Round(avgScores.Sum(), 1);

        var grade = TestQualityReport.ComputeGrade(total);

        return new TestQualityReport
        {
            TestFile = _testFile,
            TestsAnalyzed = testScores.Count,
            TotalScore = total,
            Grade = grade,
            Pillars = pillars,
            Issues = issues,
            TestScores = testScores
        };
    }

    private TestMethodScore ScoreTestMethod(MethodDeclarationSyntax method, SyntaxNode root, List<TestIssue> issues)
    {
        var scores = new double[QualityAttributes.Count];
        var name = method.Identifier.Text;
        var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var body = method.Body?.ToFullString() ?? method.ExpressionBody?.ToFullString() ?? "";

        // ── Trustworthiness ──────────────────────────────────────

        // 1. Determinism
        scores[QualityAttributes.Determinism] = ScoreDeterminism(body, name, line, issues);

        // 2. Isolation
        scores[QualityAttributes.Isolation] = ScoreIsolation(method, root, name, line, issues);

        // 3. Correct Oracle
        scores[QualityAttributes.CorrectOracle] = ScoreOracle(body, name, line, issues);

        // 4. Signal Integrity (heuristic — can't know without mutation data)
        scores[QualityAttributes.SignalIntegrity] = ScoreSignalIntegrity(body, name);

        // ── Precision ────────────────────────────────────────────

        // 5. Single Concern
        scores[QualityAttributes.SingleConcern] = ScoreSingleConcern(body, name, line, issues);

        // 6. Behavioral Focus
        scores[QualityAttributes.BehavioralFocus] = ScoreBehavioralFocus(body, name, line, issues);

        // 7. Boundary Coverage
        scores[QualityAttributes.BoundaryCoverage] = ScoreBoundaryCoverage(method, name, line, issues);

        // 8. Meaningful Scope
        scores[QualityAttributes.MeaningfulScope] = ScoreMeaningfulScope(body, name);

        // ── Maintainability ──────────────────────────────────────

        // 9. Readability
        scores[QualityAttributes.Readability] = ScoreReadability(method, name, body, line, issues);

        // 10. Refactor Resistance
        scores[QualityAttributes.RefactorResistance] = ScoreRefactorResistance(body, name, line, issues);

        // 11. Setup Simplicity
        scores[QualityAttributes.SetupSimplicity] = ScoreSetupSimplicity(body, name, line, issues);

        // 12. Independence
        scores[QualityAttributes.Independence] = ScoreIndependence(method, root, name);

        // ── Diagnostic Value ─────────────────────────────────────

        // 13. Failure Localization
        scores[QualityAttributes.FailureLocalization] = ScoreFailureLocalization(body, name, line, issues);

        // 14. Execution Speed
        scores[QualityAttributes.ExecutionSpeed] = ScoreSpeed(body, name, line, issues);

        // 15. Regression Detection
        scores[QualityAttributes.RegressionDetection] = ScoreRegressionDetection(body, name);

        // 16. Error Message Quality
        scores[QualityAttributes.ErrorMessageQuality] = ScoreErrorMessages(body, name, line, issues);

        return new TestMethodScore
        {
            TestName = name,
            LineNumber = line,
            AttributeScores = scores
        };
    }

    // ── Attribute Scorers ────────────────────────────────────────

    private double ScoreDeterminism(string body, string name, int line, List<TestIssue> issues)
    {
        var infraPatterns = new[] { "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now",
            "Random()", "Random.Shared", "Environment.TickCount",
            "Guid.NewGuid", "Thread.Sleep", "Task.Delay" };

        var found = infraPatterns.Where(p => body.Contains(p)).ToList();
        if (found.Count > 0)
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "determinism",
                Smell = "The Slowpoke", LineNumber = line,
                Description = $"Uses non-deterministic calls: {string.Join(", ", found)}", Score = 0 });
            return 0;
        }

        // Check for file/network I/O
        var ioPatterns = new[] { "HttpClient", "WebClient", "SqlConnection", "DbContext" };
        if (ioPatterns.Any(p => body.Contains(p)))
            return 1;

        // File I/O is ok for integration-style tests if in temp dirs
        if (body.Contains("File.") || body.Contains("Directory."))
            return 2;

        return 3; // Fully deterministic
    }

    private double ScoreIsolation(MethodDeclarationSyntax method, SyntaxNode root, string name, int line, List<TestIssue> issues)
    {
        var classDecl = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is null) return 2;

        // Check for static mutable fields
        var staticFields = classDecl.Members.OfType<FieldDeclarationSyntax>()
            .Where(f => f.Modifiers.Any(SyntaxKind.StaticKeyword) && !f.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
            .ToList();

        if (staticFields.Count > 0)
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "isolation",
                LineNumber = line, Description = "Test class has mutable static fields — shared state risk", Score = 0 });
            return 0;
        }

        // IDisposable test class = proper cleanup
        var implementsDisposable = classDecl.BaseList?.Types
            .Any(t => t.ToString().Contains("IDisposable")) ?? false;

        // Has instance fields but no Dispose
        var instanceFields = classDecl.Members.OfType<FieldDeclarationSyntax>()
            .Where(f => !f.Modifiers.Any(SyntaxKind.StaticKeyword) && !f.Modifiers.Any(SyntaxKind.ConstKeyword))
            .ToList();

        if (instanceFields.Count > 0 && !implementsDisposable)
            return 2;

        return 3;
    }

    private double ScoreOracle(string body, string name, int line, List<TestIssue> issues)
    {
        // The Liar: Assert.True(true) or no assertions at all
        if (!body.Contains("Assert"))
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "correct_oracle",
                Smell = "The Liar", LineNumber = line,
                Description = "No assertions found — test always passes", Score = 0 });
            return 0;
        }

        // Weak assertions: Assert.True without meaningful condition
        if (body.Contains("Assert.True(true)") || body.Contains("Assert.False(false)"))
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "correct_oracle",
                Smell = "The Liar", LineNumber = line,
                Description = "Vacuous assertion: Assert.True(true)", Score = 0 });
            return 0;
        }

        // Assert.NotNull only (weak oracle)
        var assertCalls = CountPatternOccurrences(body, "Assert.");
        var notNullCalls = CountPatternOccurrences(body, "Assert.NotNull");
        if (assertCalls == notNullCalls && assertCalls > 0)
            return 1;

        // Strong assertions: Assert.Equal, Assert.Contains, Assert.Throws
        if (body.Contains("Assert.Equal") || body.Contains("Assert.Throws") || body.Contains("Assert.Contains"))
            return 3;

        return 2;
    }

    private double ScoreSignalIntegrity(string body, string name)
    {
        // Without mutation data, heuristic based on assertion specificity
        if (!body.Contains("Assert")) return 0;
        if (body.Contains("Assert.Equal") && body.Contains("Assert.Throws")) return 3;
        if (body.Contains("Assert.Equal") || body.Contains("Assert.Throws")) return 2;
        return 1;
    }

    private double ScoreSingleConcern(string body, string name, int line, List<TestIssue> issues)
    {
        var assertCount = CountPatternOccurrences(body, "Assert.");
        if (assertCount > 5)
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "single_concern",
                Smell = "The Giant", LineNumber = line,
                Description = $"{assertCount} assertions — split into separate tests", Score = 0 });
            return 0;
        }
        if (assertCount > 3) return 1;
        if (assertCount > 1) return 2;
        return 3;
    }

    private double ScoreBehavioralFocus(string body, string name, int line, List<TestIssue> issues)
    {
        // Detect over-specified mocks
        if (body.Contains("Verify(") && body.Contains("Times."))
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "behavioral_focus",
                Smell = "The Mockery", LineNumber = line,
                Description = "Verifies mock call counts — tests HOW not WHAT", Score = 1 });
            return 1;
        }

        // Reflection access to private members
        if (body.Contains("BindingFlags.NonPublic") || body.Contains("GetField(") || body.Contains("GetProperty("))
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "behavioral_focus",
                Smell = "The Twin", LineNumber = line,
                Description = "Accesses private members via reflection — tests implementation details", Score = 0 });
            return 0;
        }

        return 3;
    }

    private double ScoreBoundaryCoverage(MethodDeclarationSyntax method, string name, int line, List<TestIssue> issues)
    {
        var hasTheory = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString().Contains("Theory"));

        var hasInlineData = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString().Contains("InlineData") || a.Name.ToString().Contains("MemberData"));

        if (hasTheory && hasInlineData) return 3;
        if (hasTheory) return 2;

        // [Fact] only — check if name suggests boundary testing
        if (name.Contains("Boundary") || name.Contains("Edge") || name.Contains("Empty") ||
            name.Contains("Null") || name.Contains("Zero") || name.Contains("Max") || name.Contains("Min"))
            return 2;

        issues.Add(new TestIssue { TestName = name, Attribute = "boundary_coverage",
            LineNumber = line,
            Description = "No [Theory]/[InlineData] — consider parameterized tests for boundary values" });
        return 1;
    }

    private double ScoreMeaningfulScope(string body, string name)
    {
        // Trivial: only testing a property getter
        if (body.Length < 100 && CountPatternOccurrences(body, "Assert.") == 1)
        {
            if (body.Contains(".Name") || body.Contains(".Id") || body.Contains(".Count"))
                return 1;
        }

        // Tests with Throws or complex logic are meaningful
        if (body.Contains("Assert.Throws") || body.Contains("Assert.Contains") ||
            body.Contains("Assert.Equal") || body.Length > 200)
            return 3;

        return 2;
    }

    private double ScoreReadability(MethodDeclarationSyntax method, string name, string body, int line, List<TestIssue> issues)
    {
        double score = 0;

        // Naming: Method_Scenario_Expected pattern
        var parts = name.Split('_');
        if (parts.Length >= 3) score += 1;
        else if (parts.Length >= 2) score += 0.5;
        else
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "readability",
                LineNumber = line,
                Description = "Name doesn't follow Method_Scenario_Expected pattern" });
        }

        // AAA structure: check for comments or blank-line separation
        if (body.Contains("// Arrange") || body.Contains("// Act") || body.Contains("// Assert"))
            score += 1;
        else if (body.Contains("\n\n") || body.Contains("\r\n\r\n")) // blank line separation
            score += 0.5;

        // Named constants vs magic values
        if (!body.Contains("const ") && CountPatternOccurrences(body, "\"") > 4)
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "readability",
                LineNumber = line, Description = "Multiple magic strings — extract to named constants" });
        }
        else
        {
            score += 1;
        }

        return Math.Min(score, 3);
    }

    private double ScoreRefactorResistance(string body, string name, int line, List<TestIssue> issues)
    {
        if (body.Contains("BindingFlags.NonPublic"))
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "refactor_resistance",
                Smell = "The Twin", LineNumber = line,
                Description = "Tests private members — will break on any internal refactor", Score = 0 });
            return 0;
        }

        // Mock.Verify with specific call counts
        if (body.Contains("Times.Exactly") || body.Contains("Times.Once"))
            return 1;

        return 3;
    }

    private double ScoreSetupSimplicity(string body, string name, int line, List<TestIssue> issues)
    {
        var lines = LineHelpers.SplitLines(body).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var firstAssert = Array.FindIndex(lines, l => l.Contains("Assert.") || l.Contains("var actual") || l.Contains("var result"));
        var setupLines = firstAssert > 0 ? firstAssert : lines.Length / 2;

        if (setupLines > 15)
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "setup_simplicity",
                Smell = "The Giant", LineNumber = line,
                Description = $"{setupLines} setup lines — use builders/factories to reduce arrangement", Score = 0 });
            return 0;
        }
        if (setupLines > 10) return 1;
        if (setupLines > 5) return 2;
        return 3;
    }

    private double ScoreIndependence(MethodDeclarationSyntax method, SyntaxNode root, string name)
    {
        var classDecl = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is null) return 3;

        // Heavy constructor = shared state risk
        var constructor = classDecl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (constructor is not null)
        {
            var ctorBody = constructor.Body?.ToFullString() ?? "";
            var ctorLines = LineHelpers.SplitLines(ctorBody).Count(l => l.Trim().Length > 0);
            if (ctorLines > 10) return 1;
        }

        return 3;
    }

    private double ScoreFailureLocalization(string body, string name, int line, List<TestIssue> issues)
    {
        // Check for custom messages in assertions
        // Assert.Equal(expected, actual) vs Assert.True(condition, "message")
        if (body.Contains("Assert.True(") && !body.Contains("Assert.True(") )
            return 2; // can't easily parse message arg

        // Multiple asserts without messages = hard to localize
        var assertCount = CountPatternOccurrences(body, "Assert.");
        if (assertCount > 2)
        {
            // Check if any have custom messages (3rd+ arg or string after condition)
            return 1;
        }

        if (assertCount == 1) return 3; // Single assert = perfect localization
        return 2;
    }

    private double ScoreSpeed(string body, string name, int line, List<TestIssue> issues)
    {
        var slowPatterns = new[] { "Thread.Sleep", "Task.Delay", "HttpClient",
            "SqlConnection", "DbContext", "WebClient" };

        var found = slowPatterns.Where(p => body.Contains(p)).ToList();
        if (found.Count > 0)
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "execution_speed",
                Smell = "The Slowpoke", LineNumber = line,
                Description = $"Slow patterns: {string.Join(", ", found)}", Score = 0 });
            return 0;
        }

        if (body.Contains("File.") || body.Contains("Directory.") || body.Contains("Process."))
            return 1;

        return 3; // Pure in-memory
    }

    private double ScoreRegressionDetection(string body, string name)
    {
        // Heuristic: specific value assertions are better at catching regressions
        if (body.Contains("Assert.Equal") && body.Contains("Assert."))
            return 2;

        if (body.Contains("Assert.Throws"))
            return 3; // Exception testing is strong regression detection

        if (body.Contains("Assert.True") || body.Contains("Assert.False"))
            return 1;

        return 0;
    }

    private double ScoreErrorMessages(string body, string name, int line, List<TestIssue> issues)
    {
        // Best: Assert.Equal with clear variable names
        if (body.Contains("expected") && body.Contains("actual"))
            return 3;

        // Good: Assert.Equal with inline values
        if (body.Contains("Assert.Equal"))
            return 2;

        // Weak: Assert.True without context
        if (body.Contains("Assert.True(") && !body.Contains(","))
        {
            issues.Add(new TestIssue { TestName = name, Attribute = "error_message_quality",
                LineNumber = line,
                Description = "Assert.True without message — failure gives no context" });
            return 0;
        }

        return 1;
    }

    // ── Weighted scoring ─────────────────────────────────────────

    private static double ComputeWeightedScore(double[] avgScores, WeightProfile weights)
    {
        double score = weights.Bias;
        for (int i = 0; i < QualityAttributes.Count && i < weights.Weights.Length; i++)
        {
            score += avgScores[i] * weights.Weights[i];
        }
        return Math.Round(Math.Max(0, Math.Min(48, score)), 1);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static int CountPatternOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }

    // ── Test method finder ───────────────────────────────────────

    private sealed class TestMethodFinder : CSharpSyntaxWalker
    {
        public List<MethodDeclarationSyntax> TestMethods { get; } = [];

        private static readonly string[] TestAttributes = ["Fact", "Theory", "Test", "TestMethod"];

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var attrs = node.AttributeLists
                .SelectMany(al => al.Attributes)
                .Select(a => a.Name.ToString());

            if (attrs.Any(a => TestAttributes.Any(ta => a.Contains(ta))))
            {
                TestMethods.Add(node);
            }

            base.VisitMethodDeclaration(node);
        }
    }
}
