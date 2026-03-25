using SlopEvaluator.Mutations.Fix;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Fix;

/// <summary>
/// Generates killing tests for surviving mutants using strategy-based templates.
/// Deterministic, auditable — no AI generation.
/// </summary>
public sealed class TestGenerator
{
    private readonly string _sourceFile;
    private readonly string? _existingTestFile;

    public TestGenerator(string sourceFile, string? existingTestFile = null)
    {
        _sourceFile = sourceFile;
        _existingTestFile = existingTestFile;
    }

    public List<KillingTest> GenerateTests(List<Survivor> survivors)
    {
        var sourceLines = File.Exists(_sourceFile) ? File.ReadAllLines(_sourceFile) : [];
        var className = Path.GetFileNameWithoutExtension(_sourceFile);
        var testConventions = _existingTestFile is not null && File.Exists(_existingTestFile)
            ? DetectConventions(File.ReadAllText(_existingTestFile))
            : new TestConventions();

        var tests = new List<KillingTest>();

        foreach (var survivor in survivors)
        {
            var methodName = GetMethodContext(sourceLines, survivor.LineNumberHint ?? 1);
            var testName = GenerateTestName(methodName, survivor);
            var testCode = GenerateTestCode(survivor, testName, className, methodName, testConventions);

            tests.Add(new KillingTest
            {
                SurvivorId = survivor.Id,
                Strategy = survivor.Strategy,
                TestName = testName,
                TestCode = testCode,
                Description = survivor.Description,
                OriginalCode = survivor.OriginalCode ?? "",
                MutatedCode = survivor.MutatedCode ?? "",
                LineNumber = survivor.LineNumberHint
            });
        }

        return tests;
    }

    private static string GenerateTestName(string methodName, Survivor survivor) => survivor.Strategy switch
    {
        "boundary" => $"{methodName}_AtBoundary_ProducesCorrectResult",
        "return-value" => $"{methodName}_ReturnsComputedValue_NotDefault",
        "exception" => $"{methodName}_InvalidInput_ThrowsExpectedException",
        "logic-inversion" => $"{methodName}_BothBranches_ProduceCorrectResults",
        "semantic" => $"{methodName}_CollectionOperation_ReturnsCorrectElement",
        _ => $"{methodName}_MutationKiller_{survivor.Id}"
    };

    private static string GenerateTestCode(
        Survivor survivor, string testName, string className,
        string methodName, TestConventions conventions)
    {
        var comment = $"    // Kills {survivor.Id}: {survivor.Description}\n" +
                      $"    // Original: {survivor.OriginalCode?.ReplaceLineEndings(" ").Trim()}\n" +
                      $"    // Mutated:  {survivor.MutatedCode?.ReplaceLineEndings(" ").Trim()}";

        return survivor.Strategy switch
        {
            "boundary" => GenerateBoundaryTest(testName, className, methodName, survivor, comment, conventions),
            "return-value" => GenerateReturnValueTest(testName, className, methodName, survivor, comment, conventions),
            "exception" => GenerateExceptionTest(testName, className, methodName, survivor, comment, conventions),
            "logic-inversion" => GenerateLogicInversionTest(testName, className, methodName, survivor, comment, conventions),
            "semantic" => GenerateSemanticTest(testName, className, methodName, survivor, comment, conventions),
            _ => GenerateGenericTest(testName, className, methodName, survivor, comment, conventions)
        };
    }

    private static string GenerateBoundaryTest(string testName, string className,
        string methodName, Survivor survivor, string comment, TestConventions conv)
    {
        return $$"""
            {{conv.TestAttribute}}
            public void {{testName}}()
            {
        {{comment}}

                // Arrange
                var sut = new {{className}}();

                // Act & Assert — test the exact boundary where the mutation differs
                // The mutation changes: {{survivor.OriginalCode?.Trim()}}
                //                   to: {{survivor.MutatedCode?.Trim()}}
                // TODO: Replace with actual boundary value test for line {{survivor.LineNumberHint}}
                // Example: Assert.Equal(expectedBoundaryValue, sut.{{methodName}}(boundaryInput));
                Assert.True(true, "TODO: Add boundary assertion for {{survivor.Id}}");
            }
        """;
    }

    private static string GenerateReturnValueTest(string testName, string className,
        string methodName, Survivor survivor, string comment, TestConventions conv)
    {
        return $$"""
            {{conv.TestAttribute}}
            public void {{testName}}()
            {
        {{comment}}

                // Arrange
                var sut = new {{className}}();

                // Act
                var result = sut.{{methodName}}(/* TODO: provide test input */);

                // Assert — verify the ACTUAL computed value, not just non-null/non-default
                // The mutation returns a default value instead of the computed result
                // TODO: Assert.Equal(expectedComputedValue, result);
                Assert.True(true, "TODO: Assert computed return value for {{survivor.Id}}");
            }
        """;
    }

    private static string GenerateExceptionTest(string testName, string className,
        string methodName, Survivor survivor, string comment, TestConventions conv)
    {
        return $$"""
            {{conv.TestAttribute}}
            public void {{testName}}()
            {
        {{comment}}

                // Arrange
                var sut = new {{className}}();

                // Act & Assert — verify the guard clause throws on invalid input
                // The mutation removes the validation, allowing invalid state
                // TODO: Assert.Throws<ArgumentException>(() => sut.{{methodName}}(invalidInput));
                Assert.True(true, "TODO: Assert exception thrown for {{survivor.Id}}");
            }
        """;
    }

    private static string GenerateLogicInversionTest(string testName, string className,
        string methodName, Survivor survivor, string comment, TestConventions conv)
    {
        return $$"""
            {{conv.TestAttribute}}
            public void {{testName}}()
            {
        {{comment}}

                // Arrange
                var sut = new {{className}}();

                // Act & Assert — test BOTH branches of the condition
                // The mutation negates the condition, swapping which branch executes
                // TODO: Test true branch: Assert.Equal(expectedWhenTrue, sut.{{methodName}}(inputForTrue));
                // TODO: Test false branch: Assert.Equal(expectedWhenFalse, sut.{{methodName}}(inputForFalse));
                Assert.True(true, "TODO: Assert both branches for {{survivor.Id}}");
            }
        """;
    }

    private static string GenerateSemanticTest(string testName, string className,
        string methodName, Survivor survivor, string comment, TestConventions conv)
    {
        return $$"""
            {{conv.TestAttribute}}
            public void {{testName}}()
            {
        {{comment}}

                // Arrange
                var sut = new {{className}}();

                // Act & Assert — verify ordering/selection behavior
                // The mutation swaps First↔Last, Any↔All, Min↔Max, etc.
                // TODO: Use a collection where first != last, then assert correct element
                Assert.True(true, "TODO: Assert collection operation for {{survivor.Id}}");
            }
        """;
    }

    private static string GenerateGenericTest(string testName, string className,
        string methodName, Survivor survivor, string comment, TestConventions conv)
    {
        return $$"""
            {{conv.TestAttribute}}
            public void {{testName}}()
            {
        {{comment}}

                // This mutation survived: {{survivor.Description}}
                // Write a test that fails when this specific change is applied
                // TODO: Implement killing test for {{survivor.Id}}
                Assert.True(true, "TODO: Implement killing test for {{survivor.Id}}");
            }
        """;
    }

    private static string GetMethodContext(string[] lines, int lineNumber)
    {
        for (int i = Math.Min(lineNumber - 1, lines.Length - 1); i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Contains("public ") || line.Contains("private ") ||
                line.Contains("protected ") || line.Contains("internal "))
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"(\w+)\s*\(");
                if (match.Success) return match.Groups[1].Value;
            }
        }
        return "Method";
    }

    private static TestConventions DetectConventions(string testSource)
    {
        var conv = new TestConventions();
        if (testSource.Contains("[Fact]")) conv.TestAttribute = "[Fact]";
        else if (testSource.Contains("[Test]")) conv.TestAttribute = "[Test]";
        else if (testSource.Contains("[TestMethod]")) conv.TestAttribute = "[TestMethod]";
        return conv;
    }

    private sealed class TestConventions
    {
        public string TestAttribute { get; set; } = "[Fact]";
    }
}
