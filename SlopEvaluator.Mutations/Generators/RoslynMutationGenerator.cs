using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Strategies;
using MutationCandidate = SlopEvaluator.Mutations.Models.MutationCandidate;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Generators;

/// <summary>
/// Generates mutations by walking the Roslyn AST — zero false positives from
/// strings, comments, or XML docs. Now also delegates to pluggable IMutationStrategy
/// implementations for structural mutations (remove-statement, swap-statements, etc.).
/// </summary>
public sealed class RoslynMutationGenerator
{
    private readonly string _sourceFile;
    private readonly Action<string> _log;
    private readonly IReadOnlyList<IMutationStrategy> _strategies;

    public RoslynMutationGenerator(string sourceFile, Action<string>? log = null)
    {
        _sourceFile = Path.GetFullPath(sourceFile);
        _log = log ?? Console.WriteLine;

        if (!File.Exists(_sourceFile))
            throw new FileNotFoundException($"Source file not found: {_sourceFile}");

        _strategies = new IMutationStrategy[]
        {
            new RemoveStatementStrategy(),
            new SwapStatementsStrategy(),
            new EmptyMethodBodyStrategy(),
            new RemoveGuardClauseStrategy()
        };
    }

    public HarnessConfig Generate(string testCommand, string? projectPath = null,
        int maxMutations = 15, TrendReport? trendHistory = null)
    {
        var source = File.ReadAllText(_sourceFile);
        var tree = CSharpSyntaxTree.ParseText(source, path: _sourceFile);
        var root = tree.GetRoot();

        _log("  Analyzing source with Roslyn AST...");

        // Phase 1: Walk AST with the existing visitor-based finder
        var finder = new MutationFinder();
        finder.Visit(root);

        var candidates = new List<MutationCandidate>(finder.Candidates);
        _log($"  Found {candidates.Count} mutation candidates via AST walker");

        // Phase 2: Run pluggable structural strategies
        foreach (var strategy in _strategies)
        {
            var strategyCandidates = strategy.FindCandidates(tree, root, _sourceFile);
            candidates.AddRange(strategyCandidates);
            if (strategyCandidates.Count > 0)
                _log($"  Found {strategyCandidates.Count} candidates via {strategy.Name} strategy");
        }

        _log($"  Total: {candidates.Count} mutation candidates");

        // Pre-validation: filter out mutations that won't compile
        var validated = ValidateMutations(candidates, source);
        _log($"  {validated.Count}/{candidates.Count} mutations passed pre-validation");

        // Deduplicate by line number — prefer boundary/exception over return-value/logic
        var strategyPriority = new Dictionary<string, int>
        {
            // Walker-detected strategies (fine-grained, high priority)
            ["boundary"] = 1, ["boolean"] = 2, ["exception"] = 3,
            ["null-coalescing"] = 4, ["increment"] = 5, ["compound-assignment"] = 6,
            ["string"] = 7, ["linq-chain"] = 8, ["async"] = 9, ["semantic"] = 10,
            ["logic-inversion"] = 11, ["return-value"] = 12,
            // Structural strategies (coarse-grained, lower priority)
            ["remove-guard"] = 13, ["remove-statement"] = 14,
            ["swap-statements"] = 15, ["empty-method-body"] = 16
        };
        var deduped = validated
            .GroupBy(c => c.LineNumber)
            .Select(g => g.OrderBy(c => strategyPriority.GetValueOrDefault(c.Strategy, 99)).First())
            .ToList();

        // Smart mode: reorder by survival rates + method complexity.
        // Activates when --smart is used (trendHistory is non-null), even without prior runs.
        if (trendHistory is not null)
        {
            var complexity = ComplexityAnalyzer.AnalyzeSource(source);
            var hasHistory = trendHistory.Entries.Count > 0;

            if (complexity.Count > 0)
            {
                var prioritizer = new MutationPrioritizer();
                deduped = prioritizer.PrioritizeWithComplexity(deduped, trendHistory, complexity).ToList();

                var hotspots = complexity.Take(3).Select(m => $"{m.MethodName}(CC={m.CyclomaticComplexity})");
                _log($"  Smart mode: complexity hotspots — {string.Join(", ", hotspots)}");
                _log($"  Applied smart mutation selection ({(hasHistory ? "trend + complexity" : "complexity only")})");
            }
            else if (hasHistory)
            {
                var prioritizer = new MutationPrioritizer();
                deduped = prioritizer.Prioritize(deduped, trendHistory).ToList();
                _log("  Applied smart mutation selection (trend only)");
            }
            else
            {
                _log("  Smart mode: no history or complexity data yet — using default ordering");
            }
        }

        deduped = deduped.Take(maxMutations).ToList();

        // Build MutationSpecs
        var mutations = new List<MutationSpec>();
        for (int i = 0; i < deduped.Count; i++)
        {
            var c = deduped[i];
            mutations.Add(new MutationSpec
            {
                Id = $"ROSLYN{i + 1:D2}",
                Strategy = c.Strategy,
                Description = c.Description,
                OriginalCode = c.OriginalCode,
                MutatedCode = c.MutatedCode,
                RiskLevel = c.RiskLevel,
                LineNumberHint = c.LineNumber,
                TargetMethod = c.TargetMethod,
                NodeIndex = c.NodeIndex,
                Parameters = c.Parameters
            });
        }

        _log($"  Selected {mutations.Count} mutations (max {maxMutations})");

        return new HarnessConfig
        {
            SourceFile = _sourceFile,
            ProjectPath = projectPath ?? "",
            TestCommand = testCommand,
            Target = Path.GetFileNameWithoutExtension(_sourceFile),
            TestTimeoutSeconds = 120,
            ReportPath = Path.ChangeExtension(_sourceFile, ".mutation-report.json"),
            Mutations = mutations
        };
    }

    /// <summary>
    /// Applies per-method mutation caps based on complexity budget.
    /// High-complexity methods keep more mutations; low-complexity methods are capped.
    /// </summary>
    private static List<MutationCandidate> ApplyBudgetCaps(
        List<MutationCandidate> candidates,
        Dictionary<string, int> budget,
        List<MethodComplexity> complexity)
    {
        if (budget.Count == 0)
            return candidates;

        // Build line-range → method key lookup
        var methodRanges = complexity
            .Select(m => (key: $"{m.ClassName}.{m.MethodName}", start: m.LineNumber, end: m.LineNumber + m.LineCount))
            .ToList();

        var perMethodCounts = new Dictionary<string, int>();
        var result = new List<MutationCandidate>();

        foreach (var c in candidates)
        {
            var methodKey = methodRanges
                .FirstOrDefault(r => c.LineNumber >= r.start && c.LineNumber <= r.end)
                .key ?? "unknown";

            var count = perMethodCounts.GetValueOrDefault(methodKey, 0);
            var cap = budget.GetValueOrDefault(methodKey, 3); // default 3 for unknown methods

            if (count < cap)
            {
                result.Add(c);
                perMethodCounts[methodKey] = count + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Filters out mutation candidates that are likely to produce compile errors.
    /// Uses pattern-based blocklist rules + Roslyn parse validation.
    /// </summary>
    private List<MutationCandidate> ValidateMutations(List<MutationCandidate> candidates, string source)
    {
        var valid = new List<MutationCandidate>();

        foreach (var candidate in candidates)
        {
            // Reject equivalent mutants — identical original/mutated code is a no-op
            if (candidate.OriginalCode.Trim() == candidate.MutatedCode.Trim())
            {
                _log($"    [skip] {candidate.Strategy} at line {candidate.LineNumber}: equivalent mutant (identical code)");
                continue;
            }

            // Reject known equivalent patterns: .Count >= 0, .Length >= 0 (always true for collections)
            if (IsKnownEquivalentPattern(candidate))
            {
                _log($"    [skip] {candidate.Strategy} at line {candidate.LineNumber}: likely equivalent mutant ({candidate.Description})");
                continue;
            }

            // Pattern-based blocklist: skip known-bad patterns before expensive parse check
            if (!PassesPatternCheck(candidate, source))
            {
                _log($"    [skip] {candidate.Strategy} at line {candidate.LineNumber}: blocked by pattern rule");
                continue;
            }

            // Syntax parse validation: apply mutation and check for parse errors
            if (!PassesParseCheck(candidate, source))
            {
                _log($"    [skip] {candidate.Strategy} at line {candidate.LineNumber}: parse errors in mutated source");
                continue;
            }

            valid.Add(candidate);
        }

        return valid;
    }

    /// <summary>
    /// Pattern-based rules that reject mutations known to cause compile errors.
    /// </summary>
    private static bool PassesPatternCheck(MutationCandidate candidate, string source)
    {
        switch (candidate.Strategy)
        {
            case "async":
                // Don't remove 'await' if the result is assigned to a variable.
                // e.g. "var x = await Foo()" → "var x = Foo()" produces Task<T> vs T mismatch.
                return !IsAwaitResultUsed(candidate, source);

            case "return-value":
                // Don't use 'default!' for records, value tuples, or generic types with constraints
                // that may have required init properties.
                if (candidate.MutatedCode.Contains("default!"))
                    return !IsProblematicDefaultType(candidate, source);
                return true;

            case "linq-chain":
                // Don't remove .Where() if the replacement line is empty or would break the chain
                if (candidate.Description.Contains("Remove .Where()"))
                    return !string.IsNullOrWhiteSpace(candidate.MutatedCode)
                        && candidate.MutatedCode != candidate.OriginalCode;
                return true;

            default:
                return true;
        }
    }

    /// <summary>
    /// Detects mutations that produce semantically equivalent code (always true/false).
    /// </summary>
    internal static bool IsKnownEquivalentPattern(MutationCandidate candidate)
    {
        var mutated = candidate.MutatedCode?.Trim() ?? "";

        // .Count >= 0 or .Length >= 0 — always true for collections (count is never negative)
        if (mutated.Contains(".Count >= 0") || mutated.Contains(".Length >= 0")
            || mutated.Contains(".Count() >= 0"))
            return true;

        // .Count < 0 or .Length < 0 — always false for collections
        if (mutated.Contains(".Count < 0") || mutated.Contains(".Length < 0")
            || mutated.Contains(".Count() < 0"))
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether removing 'await' would cause a type mismatch because
    /// the awaited result is assigned or used in an expression.
    /// </summary>
    private static bool IsAwaitResultUsed(MutationCandidate candidate, string source)
    {
        // Find the line in source
        var lines = LineHelpers.SplitLines(source);
        if (candidate.LineNumber < 1 || candidate.LineNumber > lines.Length)
            return true; // Be conservative — block it

        var line = lines[candidate.LineNumber - 1].Trim();

        // Pattern: "var x = await ...", "Type x = await ...", "return await ..."
        // If 'await' is preceded by '=' on the same line, result is assigned
        var awaitIdx = line.IndexOf("await ", StringComparison.Ordinal);
        if (awaitIdx < 0) return false;

        var beforeAwait = line[..awaitIdx].TrimEnd();

        // Assignment patterns: "= await", "var x = await", etc.
        if (beforeAwait.EndsWith("="))
            return true;

        // Return pattern: "return await" — removing await changes return type from T to Task<T>
        if (beforeAwait.EndsWith("return"))
            return true;

        // Used as argument: "Method(await expr)" — removing changes type
        if (beforeAwait.EndsWith("(") || beforeAwait.EndsWith(","))
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether 'default!' is problematic for the return type at the mutation's line.
    /// Records with required init, value tuples, and complex generic types often fail.
    /// </summary>
    private static bool IsProblematicDefaultType(MutationCandidate candidate, string source)
    {
        // Look at the method's return type by scanning upward from the mutation line
        var lines = LineHelpers.SplitLines(source);
        if (candidate.LineNumber < 1 || candidate.LineNumber > lines.Length)
            return true;

        // Scan upward for a method/property declaration to find the return type
        for (int i = candidate.LineNumber - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("///"))
                continue;

            // Value tuples: (int, string), (T1, T2, T3)
            if (line.Contains("(") && line.Contains(",") && line.Contains(")") && !line.Contains("=>"))
            {
                // Check if this looks like a tuple return type
                var parenIdx = line.IndexOf('(');
                var closeIdx = line.IndexOf(')');
                if (parenIdx < closeIdx && closeIdx < line.Length - 1)
                {
                    var afterParen = line[(closeIdx + 1)..].TrimStart();
                    if (afterParen.Length > 0 && char.IsLetter(afterParen[0]))
                        return true; // Likely a tuple return type method declaration
                }
            }

            // If we find a method declaration, check the return type
            if (line.Contains('(') && (line.Contains("public") || line.Contains("private")
                || line.Contains("internal") || line.Contains("protected") || line.Contains("static")))
            {
                // Check for record/struct return types that often have required inits
                // Also check for ImmutableArray, ImmutableList, etc.
                if (line.Contains("record ") || line.Contains("Record")
                    || line.Contains("Immutable"))
                    return true;

                break; // Found the containing method, stop scanning
            }

            // Don't scan past class/struct boundaries
            if (line.Contains("class ") || line.Contains("struct ") || line.Contains("interface "))
                break;
        }

        return false;
    }

    /// <summary>
    /// Applies the mutation to the source text and checks for Roslyn parse errors.
    /// This catches syntax-level issues (unbalanced braces, invalid tokens, etc.).
    /// </summary>
    private static bool PassesParseCheck(MutationCandidate candidate, string source)
    {
        // Apply the mutation via string replacement (same mechanism the harness uses at runtime)
        var mutatedSource = source.Replace(candidate.OriginalCode, candidate.MutatedCode);

        // If the replacement didn't change anything, the mutation is a no-op — skip it
        if (mutatedSource == source)
            return false;

        var tree = CSharpSyntaxTree.ParseText(mutatedSource);
        var diagnostics = tree.GetDiagnostics();

        // Only reject on Error-level diagnostics (warnings are fine)
        return !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// Walks the Roslyn syntax tree to find mutable patterns.
    /// </summary>
    private sealed class MutationFinder : CSharpSyntaxWalker
    {
        public List<MutationCandidate> Candidates { get; } = [];

        // ── Binary expressions: boundary + arithmetic ────────────────

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var kind = node.OperatorToken.Kind();
            var flipped = FlipBinaryOperator(kind);
            var swapped = SwapArithmeticOperator(kind);

            // Boolean operator mutations: && ↔ ||
            var boolSwapped = SwapBooleanOperator(kind);
            if (boolSwapped is not null)
            {
                var line = GetLine(node);
                var originalLine = GetContainingLine(node);
                var op = node.OperatorToken.Text;
                var newOp = SyntaxFacts.GetText(boolSwapped.Value);

                Candidates.Add(new MutationCandidate
                {
                    Strategy = "boolean",
                    Description = $"Swap '{op}' with '{newOp}' at line {line}",
                    OriginalCode = originalLine,
                    MutatedCode = ReplaceFirst(originalLine, $" {op} ", $" {newOp} "),
                    RiskLevel = "high",
                    LineNumber = line
                });
            }

            if (flipped is not null)
            {
                var line = GetLine(node);
                var originalLine = GetContainingLine(node);
                var op = node.OperatorToken.Text;
                var newOp = SyntaxFacts.GetText(flipped.Value);

                Candidates.Add(new MutationCandidate
                {
                    Strategy = "boundary",
                    Description = $"Flip '{op}' to '{newOp}' at line {line}",
                    OriginalCode = originalLine,
                    MutatedCode = ReplaceFirst(originalLine, $" {op} ", $" {newOp} "),
                    RiskLevel = "high",
                    LineNumber = line
                });
            }
            else if (swapped is not null)
            {
                var line = GetLine(node);
                var originalLine = GetContainingLine(node);
                var op = node.OperatorToken.Text;
                var newOp = SyntaxFacts.GetText(swapped.Value);

                Candidates.Add(new MutationCandidate
                {
                    Strategy = "boundary",
                    Description = $"Swap '{op}' with '{newOp}' at line {line}",
                    OriginalCode = originalLine,
                    MutatedCode = ReplaceFirst(originalLine, $" {op} ", $" {newOp} "),
                    RiskLevel = "medium",
                    LineNumber = line
                });
            }

            // Null coalescing: x ?? y
            if (kind == SyntaxKind.QuestionQuestionToken)
            {
                VisitCoalesceExpression(node);
            }

            base.VisitBinaryExpression(node);
        }

        // ── Return statements: return default ────────────────────────

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression is null)
            {
                base.VisitReturnStatement(node);
                return;
            }

            // Check local function first (innermost scope), then method
            var localFunc = node.Ancestors().OfType<LocalFunctionStatementSyntax>().FirstOrDefault();
            var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var returnType = localFunc?.ReturnType.ToString() ?? method?.ReturnType.ToString();

            if (returnType is null)
            {
                base.VisitReturnStatement(node);
                return;
            }

            var defaultValue = GetDefaultForType(returnType);
            var line = GetLine(node);
            var originalText = node.ToFullString().Trim();

            Candidates.Add(new MutationCandidate
            {
                Strategy = "return-value",
                Description = $"Return default value instead of computed result at line {line}",
                OriginalCode = originalText,
                MutatedCode = $"return {defaultValue};",
                RiskLevel = "high",
                LineNumber = line
            });

            base.VisitReturnStatement(node);
        }

        // ── Arrow expression clauses: expression-bodied members ──────

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            var parent = node.Parent;
            string? returnType = parent switch
            {
                MethodDeclarationSyntax m => m.ReturnType.ToString(),
                PropertyDeclarationSyntax p => p.Type.ToString(),
                _ => null
            };

            if (returnType is not null && returnType != "void")
            {
                var defaultValue = GetDefaultForType(returnType);
                var line = GetLine(node);
                var originalExpr = node.Expression.ToFullString().Trim();

                Candidates.Add(new MutationCandidate
                {
                    Strategy = "return-value",
                    Description = $"Expression body returns default at line {line}",
                    OriginalCode = $"=> {originalExpr}",
                    MutatedCode = $"=> {defaultValue}",
                    RiskLevel = "high",
                    LineNumber = line
                });
            }

            base.VisitArrowExpressionClause(node);
        }

        // ── If statements: guard clauses + logic inversion ───────────

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            // Guard clause: if (...) throw ...;
            if (node.Statement is ThrowStatementSyntax
                || (node.Statement is BlockSyntax block
                    && block.Statements.Count == 1
                    && block.Statements[0] is ThrowStatementSyntax))
            {
                var line = GetLine(node);
                var guardText = node.ToFullString().Trim();
                // Take just the first line for single-line guards
                var firstLine = LineHelpers.SplitLines(guardText)[0].Trim();

                Candidates.Add(new MutationCandidate
                {
                    Strategy = "exception",
                    Description = $"Remove guard clause at line {line}",
                    OriginalCode = firstLine,
                    MutatedCode = "// guard clause removed",
                    RiskLevel = "high",
                    LineNumber = line
                });
            }
            else
            {
                // Logic inversion: negate the condition
                var line = GetLine(node);
                var condition = node.Condition.ToFullString().Trim();

                // Skip trivial conditions
                if (condition.Length > 3)
                {
                    var negated = condition.StartsWith("!")
                        ? condition[1..].Trim().TrimStart('(').TrimEnd(')')
                        : $"!({condition})";

                    Candidates.Add(new MutationCandidate
                    {
                        Strategy = "logic-inversion",
                        Description = $"Negate condition at line {line}",
                        OriginalCode = $"if ({condition})",
                        MutatedCode = $"if ({negated})",
                        RiskLevel = "medium",
                        LineNumber = line
                    });
                }
            }

            base.VisitIfStatement(node);
        }

        // ── Invocations: LINQ method swaps ───────────────────────────

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.Text;
                var swapped = SwapLinqMethod(methodName);

                if (swapped is not null)
                {
                    var line = GetLine(node);

                    Candidates.Add(new MutationCandidate
                    {
                        Strategy = "semantic",
                        Description = $"Swap .{methodName}() with .{swapped}() at line {line}",
                        OriginalCode = $".{methodName}(",
                        MutatedCode = $".{swapped}(",
                        RiskLevel = "medium",
                        LineNumber = line
                    });
                }

                // String method swaps: .ToLower() ↔ .ToUpper(), .StartsWith() ↔ .EndsWith()
                var stringSwapped = SwapStringMethod(methodName);
                if (stringSwapped is not null)
                {
                    var line = GetLine(node);
                    Candidates.Add(new MutationCandidate
                    {
                        Strategy = "string",
                        Description = $"Swap .{methodName}() with .{stringSwapped}() at line {line}",
                        OriginalCode = $".{methodName}(",
                        MutatedCode = $".{stringSwapped}(",
                        RiskLevel = "medium",
                        LineNumber = line
                    });
                }

                // LINQ chain mutations: .FirstOrDefault() → .First(), remove .Where()
                var dangerousUpgrade = UpgradeToThrowing(methodName);
                if (dangerousUpgrade is not null)
                {
                    var line = GetLine(node);
                    Candidates.Add(new MutationCandidate
                    {
                        Strategy = "linq-chain",
                        Description = $"Upgrade .{methodName}() to .{dangerousUpgrade}() (throws instead of null) at line {line}",
                        OriginalCode = $".{methodName}(",
                        MutatedCode = $".{dangerousUpgrade}(",
                        RiskLevel = "high",
                        LineNumber = line
                    });
                }

                // Remove .Where() filter — accept any parent expression (property, method, variable)
                if (methodName == "Where" && memberAccess.Expression is not null)
                {
                    var line = GetLine(node);
                    var whereCallText = $".Where({node.ArgumentList})";
                    var fullChain = GetContainingLine(node);
                    var mutatedChain = fullChain.Replace(whereCallText, "");

                    // Only emit if the replacement actually changes something
                    if (mutatedChain != fullChain)
                    {
                        Candidates.Add(new MutationCandidate
                        {
                            Strategy = "linq-chain",
                            Description = $"Remove .Where() filter at line {line}",
                            OriginalCode = fullChain,
                            MutatedCode = mutatedChain,
                            RiskLevel = "high",
                            LineNumber = line
                        });
                    }
                }

                // Remove .Distinct()
                if (methodName == "Distinct")
                {
                    var line = GetLine(node);
                    Candidates.Add(new MutationCandidate
                    {
                        Strategy = "linq-chain",
                        Description = $"Remove .Distinct() at line {line}",
                        OriginalCode = ".Distinct()",
                        MutatedCode = "",
                        RiskLevel = "medium",
                        LineNumber = line
                    });
                }
            }

            base.VisitInvocationExpression(node);
        }

        // ── Try/catch: exception handling mutations ──────────────────

        public override void VisitTryStatement(TryStatementSyntax node)
        {
            foreach (var catchClause in node.Catches)
            {
                var line = GetLine(catchClause);
                var catchBody = catchClause.Block.ToFullString().Trim();

                // Mutation: empty the catch body (swallow exception silently)
                if (catchBody.Length > 4) // Not already empty
                {
                    Candidates.Add(new MutationCandidate
                    {
                        Strategy = "exception",
                        Description = $"Swallow exception silently at line {line}",
                        OriginalCode = LineHelpers.SplitLines(catchClause.ToFullString().Trim())[0].Trim(),
                        MutatedCode = $"catch {{ }}",
                        RiskLevel = "high",
                        LineNumber = line
                    });
                }
            }

            // Mutation: remove finally block
            if (node.Finally is not null)
            {
                var line = GetLine(node.Finally);
                var finallyText = node.Finally.ToFullString().Trim();

                // If there are catch clauses, we can safely remove the finally block.
                // If there are NO catch clauses (try-finally only), removing finally
                // requires unwrapping: replace the entire try-finally with just the try body.
                if (node.Catches.Count > 0)
                {
                    Candidates.Add(new MutationCandidate
                    {
                        Strategy = "exception",
                        Description = $"Remove finally block at line {line}",
                        OriginalCode = finallyText,
                        MutatedCode = "// finally removed",
                        RiskLevel = "high",
                        LineNumber = line
                    });
                }
                else
                {
                    // try { body } finally { cleanup } → body (unwrap the try)
                    var tryBody = node.Block.Statements.ToFullString().Trim();
                    var fullTry = node.ToFullString().Trim();
                    Candidates.Add(new MutationCandidate
                    {
                        Strategy = "exception",
                        Description = $"Remove finally block at line {line}",
                        OriginalCode = fullTry,
                        MutatedCode = tryBody,
                        RiskLevel = "high",
                        LineNumber = GetLine(node)
                    });
                }
            }

            base.VisitTryStatement(node);
        }

        // ── Await expressions: async mutations ──────────────────────

        public override void VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            var line = GetLine(node);
            var awaitedExpr = node.Expression.ToFullString().Trim();

            // Mutation: remove await (fire-and-forget)
            // Only safe when the awaited result is not used (pure fire-and-forget).
            // Skip if the await is inside an assignment, return, argument, or variable declaration.
            var parent = node.Parent;
            bool resultIsUsed = parent is EqualsValueClauseSyntax     // var x = await ...
                             || parent is ReturnStatementSyntax        // return await ...
                             || parent is ArgumentSyntax               // Method(await ...)
                             || parent is ArrowExpressionClauseSyntax   // => await ...
                             || parent is AssignmentExpressionSyntax;   // x = await ...

            if (!resultIsUsed)
            {
                Candidates.Add(new MutationCandidate
                {
                    Strategy = "async",
                    Description = $"Remove await (fire-and-forget) at line {line}",
                    OriginalCode = $"await {awaitedExpr}",
                    MutatedCode = awaitedExpr,
                    RiskLevel = "high",
                    LineNumber = line
                });
            }

            // Mutation: ConfigureAwait swap
            if (awaitedExpr.Contains("ConfigureAwait(false)"))
            {
                Candidates.Add(new MutationCandidate
                {
                    Strategy = "async",
                    Description = $"Swap ConfigureAwait(false) to true at line {line}",
                    OriginalCode = "ConfigureAwait(false)",
                    MutatedCode = "ConfigureAwait(true)",
                    RiskLevel = "medium",
                    LineNumber = line
                });
            }

            base.VisitAwaitExpression(node);
        }

        // ── String literals: empty string, wrong value ───────────────

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var value = node.Token.ValueText;
                // Only mutate non-empty string literals that aren't single chars
                if (value.Length > 1)
                {
                    var line = GetLine(node);
                    var originalLine = GetContainingLine(node);

                    // Mutation: replace with empty string
                    var emptyMutated = originalLine.Replace($"\"{value}\"", "\"\"");
                    if (emptyMutated != originalLine)
                    {
                        Candidates.Add(new MutationCandidate
                        {
                            Strategy = "string",
                            Description = $"Replace string literal with empty string at line {line}",
                            OriginalCode = originalLine,
                            MutatedCode = emptyMutated,
                            RiskLevel = "medium",
                            LineNumber = line
                        });
                    }
                }
            }

            base.VisitLiteralExpression(node);
        }

        // ── String method swaps: ToLower ↔ ToUpper, StartsWith ↔ EndsWith

        private static string? SwapStringMethod(string method) => method switch
        {
            "ToLower" => "ToUpper",
            "ToUpper" => "ToLower",
            "ToLowerInvariant" => "ToUpperInvariant",
            "ToUpperInvariant" => "ToLowerInvariant",
            "StartsWith" => "EndsWith",
            "EndsWith" => "StartsWith",
            "TrimStart" => "TrimEnd",
            "TrimEnd" => "TrimStart",
            _ => null
        };

        // ── Null coalescing: x ?? y → x, x ?? y → y ────────────────

        private void VisitCoalesceExpression(BinaryExpressionSyntax node)
        {
            var line = GetLine(node);
            var originalLine = GetContainingLine(node);
            var leftText = node.Left.ToFullString().Trim();
            var rightText = node.Right.ToFullString().Trim();

            // Mutation: x ?? y → x (ignore fallback)
            Candidates.Add(new MutationCandidate
            {
                Strategy = "null-coalescing",
                Description = $"Remove null-coalescing fallback at line {line}",
                OriginalCode = originalLine,
                MutatedCode = originalLine.Replace($"{leftText} ?? {rightText}", leftText),
                RiskLevel = "high",
                LineNumber = line
            });
        }

        // ── Postfix unary: i++ → i--, i-- → i++ ────────────────────

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            var kind = node.OperatorToken.Kind();
            SyntaxKind? swapped = kind switch
            {
                SyntaxKind.PlusPlusToken => SyntaxKind.MinusMinusToken,
                SyntaxKind.MinusMinusToken => SyntaxKind.PlusPlusToken,
                _ => null
            };

            if (swapped is not null)
            {
                var line = GetLine(node);
                var originalLine = GetContainingLine(node);
                var op = node.OperatorToken.Text;
                var newOp = SyntaxFacts.GetText(swapped.Value);

                Candidates.Add(new MutationCandidate
                {
                    Strategy = "increment",
                    Description = $"Swap '{op}' with '{newOp}' at line {line}",
                    OriginalCode = originalLine,
                    MutatedCode = ReplaceFirst(originalLine, op, newOp),
                    RiskLevel = "medium",
                    LineNumber = line
                });
            }

            base.VisitPostfixUnaryExpression(node);
        }

        // ── Prefix unary: ++i → --i, --i → ++i ─────────────────────

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            var kind = node.OperatorToken.Kind();
            SyntaxKind? swapped = kind switch
            {
                SyntaxKind.PlusPlusToken => SyntaxKind.MinusMinusToken,
                SyntaxKind.MinusMinusToken => SyntaxKind.PlusPlusToken,
                _ => null
            };

            if (swapped is not null)
            {
                var line = GetLine(node);
                var originalLine = GetContainingLine(node);
                var op = node.OperatorToken.Text;
                var newOp = SyntaxFacts.GetText(swapped.Value);

                Candidates.Add(new MutationCandidate
                {
                    Strategy = "increment",
                    Description = $"Swap '{op}' with '{newOp}' at line {line}",
                    OriginalCode = originalLine,
                    MutatedCode = ReplaceFirst(originalLine, op, newOp),
                    RiskLevel = "medium",
                    LineNumber = line
                });
            }

            base.VisitPrefixUnaryExpression(node);
        }

        // ── Compound assignments: += → -=, *= → /= ─────────────────

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            var kind = node.OperatorToken.Kind();
            SyntaxKind? swapped = kind switch
            {
                SyntaxKind.PlusEqualsToken => SyntaxKind.MinusEqualsToken,
                SyntaxKind.MinusEqualsToken => SyntaxKind.PlusEqualsToken,
                SyntaxKind.AsteriskEqualsToken => SyntaxKind.SlashEqualsToken,
                SyntaxKind.SlashEqualsToken => SyntaxKind.AsteriskEqualsToken,
                _ => null
            };

            if (swapped is not null)
            {
                var line = GetLine(node);
                var originalLine = GetContainingLine(node);
                var op = node.OperatorToken.Text;
                var newOp = SyntaxFacts.GetText(swapped.Value);

                Candidates.Add(new MutationCandidate
                {
                    Strategy = "compound-assignment",
                    Description = $"Swap '{op}' with '{newOp}' at line {line}",
                    OriginalCode = originalLine,
                    MutatedCode = ReplaceFirst(originalLine, op, newOp),
                    RiskLevel = "medium",
                    LineNumber = line
                });
            }

            base.VisitAssignmentExpression(node);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static int GetLine(SyntaxNode node) =>
            node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        private static string GetContainingLine(SyntaxNode node)
        {
            var tree = node.SyntaxTree;
            var text = tree.GetText();
            var lineSpan = node.GetLocation().GetLineSpan();
            var lineIndex = lineSpan.StartLinePosition.Line;
            return text.Lines[lineIndex].ToString().Trim();
        }

        private static string ReplaceFirst(string text, string search, string replacement)
        {
            var idx = text.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return text.Replace(search.Trim(), replacement.Trim());
            return string.Concat(text.AsSpan(0, idx), replacement, text.AsSpan(idx + search.Length));
        }

        private static SyntaxKind? SwapBooleanOperator(SyntaxKind kind) => kind switch
        {
            SyntaxKind.AmpersandAmpersandToken => SyntaxKind.BarBarToken,
            SyntaxKind.BarBarToken => SyntaxKind.AmpersandAmpersandToken,
            _ => null
        };

        private static string? UpgradeToThrowing(string method) => method switch
        {
            "FirstOrDefault" => "First",
            "SingleOrDefault" => "Single",
            "LastOrDefault" => "Last",
            _ => null
        };

        private static SyntaxKind? FlipBinaryOperator(SyntaxKind kind) => kind switch
        {
            SyntaxKind.LessThanToken => SyntaxKind.LessThanEqualsToken,
            SyntaxKind.LessThanEqualsToken => SyntaxKind.LessThanToken,
            SyntaxKind.GreaterThanToken => SyntaxKind.GreaterThanEqualsToken,
            SyntaxKind.GreaterThanEqualsToken => SyntaxKind.GreaterThanToken,
            SyntaxKind.EqualsEqualsToken => SyntaxKind.ExclamationEqualsToken,
            SyntaxKind.ExclamationEqualsToken => SyntaxKind.EqualsEqualsToken,
            _ => null
        };

        private static SyntaxKind? SwapArithmeticOperator(SyntaxKind kind) => kind switch
        {
            SyntaxKind.PlusToken => SyntaxKind.MinusToken,
            SyntaxKind.MinusToken => SyntaxKind.PlusToken,
            SyntaxKind.AsteriskToken => SyntaxKind.SlashToken,
            SyntaxKind.SlashToken => SyntaxKind.AsteriskToken,
            _ => null
        };

        private static string? SwapLinqMethod(string method) => method switch
        {
            "First" => "Last",
            "Last" => "First",
            "FirstOrDefault" => "LastOrDefault",
            "LastOrDefault" => "FirstOrDefault",
            "Any" => "All",
            "All" => "Any",
            "Min" => "Max",
            "Max" => "Min",
            "OrderBy" => "OrderByDescending",
            "OrderByDescending" => "OrderBy",
            _ => null
        };

        private static string GetDefaultForType(string returnType) => returnType switch
        {
            "int" or "long" or "double" or "float" or "decimal" or "byte" or "short"
                or "Int32" or "Int64" or "Double" or "Single" or "Decimal" or "Byte" or "Int16" => "0",
            "bool" or "Boolean" => "false",
            "string" or "String" => "\"\"",
            "char" or "Char" => "'\\0'",
            "void" => "/* no return */",
            _ when returnType.EndsWith("?") => "null",
            _ when returnType == "Task" || returnType == "ValueTask"
                => "Task.FromException(new InvalidOperationException(\"mutant\"))",
            _ when returnType.StartsWith("Task<") => TaskDefaultFor(returnType),
            _ when returnType.StartsWith("ValueTask<") => TaskDefaultFor(returnType),
            _ when returnType.StartsWith("List<") => "new()",
            _ when returnType.StartsWith("IList<") || returnType.StartsWith("ICollection<")
                || returnType.StartsWith("IEnumerable<") || returnType.StartsWith("IReadOnlyList<")
                || returnType.StartsWith("IReadOnlyCollection<") => "Array.Empty<object>()",
            _ when returnType.StartsWith("Dictionary<") || returnType.StartsWith("IDictionary<")
                || returnType.StartsWith("IReadOnlyDictionary<") => "new()",
            _ when returnType.Contains("[]") => "Array.Empty<object>()",
            // Avoid default! for value tuples — they have required fields
            _ when returnType.StartsWith("(") && returnType.Contains(",") => "default",
            _ => "default!"
        };

        /// <summary>
        /// Unwraps Task&lt;T&gt; / ValueTask&lt;T&gt; and returns a compilable default.
        /// For simple inner types, returns Task.FromResult(defaultValue).
        /// For complex inner types, falls back to default! (caught by validation).
        /// </summary>
        private static string TaskDefaultFor(string taskType)
        {
            // Extract inner type: "Task<string>" → "string"
            var openAngle = taskType.IndexOf('<');
            var closeAngle = taskType.LastIndexOf('>');
            if (openAngle < 0 || closeAngle < 0) return "default!";

            var innerType = taskType[(openAngle + 1)..closeAngle].Trim();
            var innerDefault = innerType switch
            {
                "int" or "long" or "double" or "float" or "decimal" or "byte" or "short" => "0",
                "bool" => "false",
                "string" => "\"\"",
                _ when innerType.EndsWith("?") => "null",
                _ => null
            };

            if (innerDefault is not null)
                return $"Task.FromResult<{innerType}>({innerDefault})";

            // For complex types, fall back to default! — the validation step will catch issues
            return "default!";
        }
    }

}
