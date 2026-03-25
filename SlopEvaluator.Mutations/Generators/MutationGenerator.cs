using System.Text.RegularExpressions;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Strategies;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Generators;

/// <summary>
/// Automatically generates mutation configs by scanning C# source files.
/// Uses regex-based pattern detection to find mutable code patterns and
/// generates semantically meaningful mutations.
/// </summary>
public sealed partial class MutationGenerator
{
    private readonly string _sourceFile;
    private readonly Action<string> _log;

    public MutationGenerator(string sourceFile, Action<string>? log = null)
    {
        _sourceFile = Path.GetFullPath(sourceFile);
        _log = log ?? Console.WriteLine;

        if (!File.Exists(_sourceFile))
            throw new FileNotFoundException($"Source file not found: {_sourceFile}");
    }

    /// <summary>
    /// Generates a HarnessConfig with auto-detected mutations for the source file.
    /// </summary>
    public HarnessConfig Generate(string testCommand, string? projectPath = null, int maxMutations = 15)
    {
        var source = File.ReadAllText(_sourceFile);
        var lines = LineHelpers.SplitLines(source);
        var mutations = new List<MutationSpec>();
        int id = 1;

        _log("  Analyzing source for mutable patterns...");

        // ── Strategy 1: Boundary operator flips ─────────────────────
        foreach (var match in BoundaryOpRegex().Matches(source).Cast<Match>())
        {
            var op = match.Groups[1].Value;
            var flipped = FlipOperator(op);
            if (flipped is null) continue;

            var lineNum = GetLineNumber(source, match.Index);
            var context = GetLineAt(lines, lineNum)?.Trim() ?? "";

            // Skip if inside a string literal or comment
            if (IsInsideStringOrComment(lines, lineNum)) continue;

            mutations.Add(new MutationSpec
            {
                Id = $"AUTO{id++:D2}",
                Strategy = "boundary",
                Description = $"Flip '{op}' to '{flipped}' at line {lineNum}",
                OriginalCode = context,
                MutatedCode = context.Replace(op, flipped),
                RiskLevel = "high",
                LineNumberHint = lineNum
            });
        }

        // ── Strategy 2: Return value mutations ──────────────────────
        foreach (var match in ReturnValueRegex().Matches(source).Cast<Match>())
        {
            var lineNum = GetLineNumber(source, match.Index);
            var fullLine = GetLineAt(lines, lineNum)?.Trim() ?? "";

            if (IsInsideStringOrComment(lines, lineNum)) continue;

            var returnType = DetectReturnType(lines, lineNum);
            var mutatedReturn = GetDefaultReturn(returnType, fullLine);
            if (mutatedReturn is null) continue;

            mutations.Add(new MutationSpec
            {
                Id = $"AUTO{id++:D2}",
                Strategy = "return-value",
                Description = $"Return default value instead of computed result at line {lineNum}",
                OriginalCode = fullLine,
                MutatedCode = mutatedReturn,
                RiskLevel = "high",
                LineNumberHint = lineNum
            });
        }

        // ── Strategy 3: Guard clause removal ────────────────────────
        foreach (var match in GuardClauseRegex().Matches(source).Cast<Match>())
        {
            var lineNum = GetLineNumber(source, match.Index);
            var fullLine = GetLineAt(lines, lineNum)?.Trim() ?? "";

            if (IsInsideStringOrComment(lines, lineNum)) continue;

            // Find the full if-throw block
            var guardBlock = ExtractGuardBlock(lines, lineNum);
            if (guardBlock is null) continue;

            mutations.Add(new MutationSpec
            {
                Id = $"AUTO{id++:D2}",
                Strategy = "exception",
                Description = $"Remove guard clause at line {lineNum}",
                OriginalCode = guardBlock,
                MutatedCode = "// guard clause removed",
                RiskLevel = "high",
                LineNumberHint = lineNum
            });
        }

        // ── Strategy 4: Boolean condition negation ──────────────────
        foreach (var match in ConditionRegex().Matches(source).Cast<Match>())
        {
            var lineNum = GetLineNumber(source, match.Index);
            var fullLine = GetLineAt(lines, lineNum)?.Trim() ?? "";

            if (IsInsideStringOrComment(lines, lineNum)) continue;
            // Skip guard clauses (already handled above)
            if (fullLine.Contains("throw")) continue;

            mutations.Add(new MutationSpec
            {
                Id = $"AUTO{id++:D2}",
                Strategy = "logic-inversion",
                Description = $"Negate condition at line {lineNum}",
                OriginalCode = match.Value,
                MutatedCode = match.Value.Contains("!")
                    ? match.Value.Replace("!", "")
                    : match.Value.Replace("if (", "if (!(") + ")",
                RiskLevel = "medium",
                LineNumberHint = lineNum
            });
        }

        // ── Strategy 5: LINQ method swaps ───────────────────────────
        foreach (var match in LinqMethodRegex().Matches(source).Cast<Match>())
        {
            var method = match.Groups[1].Value;
            var swapped = SwapLinqMethod(method);
            if (swapped is null) continue;

            var lineNum = GetLineNumber(source, match.Index);
            if (IsInsideStringOrComment(lines, lineNum)) continue;

            mutations.Add(new MutationSpec
            {
                Id = $"AUTO{id++:D2}",
                Strategy = "semantic",
                Description = $"Swap .{method}() with .{swapped}() at line {lineNum}",
                OriginalCode = $".{method}(",
                MutatedCode = $".{swapped}(",
                RiskLevel = "medium",
                LineNumberHint = lineNum
            });
        }

        // ── Strategy 6: Arithmetic operator swaps ───────────────────
        foreach (var match in ArithmeticRegex().Matches(source).Cast<Match>())
        {
            var op = match.Groups[1].Value;
            var swapped = SwapArithmetic(op);
            if (swapped is null) continue;

            var lineNum = GetLineNumber(source, match.Index);
            if (IsInsideStringOrComment(lines, lineNum)) continue;

            var context = GetLineAt(lines, lineNum)?.Trim() ?? "";

            mutations.Add(new MutationSpec
            {
                Id = $"AUTO{id++:D2}",
                Strategy = "boundary",
                Description = $"Swap '{op}' with '{swapped}' at line {lineNum}",
                OriginalCode = context,
                MutatedCode = ReplaceFirst(context, $" {op} ", $" {swapped} "),
                RiskLevel = "medium",
                LineNumberHint = lineNum
            });
        }

        // Deduplicate by line number (keep first per line)
        var deduped = mutations
            .GroupBy(m => m.LineNumberHint)
            .Select(g => g.First())
            .Take(maxMutations)
            .ToList();

        // Re-number IDs
        for (int i = 0; i < deduped.Count; i++)
        {
            var m = deduped[i];
            deduped[i] = new MutationSpec
            {
                Id = $"AUTO{i + 1:D2}",
                Strategy = m.Strategy,
                Description = m.Description,
                OriginalCode = m.OriginalCode,
                MutatedCode = m.MutatedCode,
                RiskLevel = m.RiskLevel,
                LineNumberHint = m.LineNumberHint
            };
        }

        _log($"  Found {mutations.Count} mutation candidates, selected {deduped.Count}");

        return new HarnessConfig
        {
            SourceFile = _sourceFile,
            ProjectPath = projectPath ?? "",
            TestCommand = testCommand,
            Target = Path.GetFileNameWithoutExtension(_sourceFile),
            TestTimeoutSeconds = 120,
            ReportPath = Path.ChangeExtension(_sourceFile, ".mutation-report.json"),
            Mutations = deduped
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static int GetLineNumber(string source, int charIndex)
    {
        int line = 1;
        for (int i = 0; i < charIndex && i < source.Length; i++)
            if (source[i] == '\n') line++;
        return line;
    }

    private static string? GetLineAt(string[] lines, int lineNumber)
    {
        if (lineNumber <= 0 || lineNumber > lines.Length) return null;
        return lines[lineNumber - 1];
    }

    private static bool IsInsideStringOrComment(string[] lines, int lineNumber)
    {
        if (lineNumber <= 0 || lineNumber > lines.Length) return false;
        var line = lines[lineNumber - 1].TrimStart();
        return line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*")
            || line.StartsWith("/*") || line.StartsWith("\"");
    }

    private static string? FlipOperator(string op) => op switch
    {
        "<" => "<=",
        "<=" => "<",
        ">" => ">=",
        ">=" => ">",
        "==" => "!=",
        "!=" => "==",
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

    private static string? SwapArithmetic(string op) => op switch
    {
        "+" => "-",
        "-" => "+",
        "*" => "/",
        "/" => "*",
        _ => null
    };

    private static string? GetDefaultReturn(string? returnType, string fullLine)
    {
        if (returnType is null) return null;

        // Expression-bodied: "=> expr;" → "=> default;"
        if (fullLine.Contains("=>"))
        {
            var arrowIdx = fullLine.IndexOf("=>");
            var before = fullLine[..(arrowIdx + 2)];
            return returnType switch
            {
                "int" or "long" or "double" or "float" or "decimal" => $"{before} 0;",
                "bool" => $"{before} false;",
                "string" => $"{before} \"\";",
                _ when returnType.EndsWith("?") => $"{before} null;",
                _ => $"{before} default!;"
            };
        }

        // Regular return: "return expr;" → "return default;"
        if (fullLine.TrimStart().StartsWith("return"))
        {
            return returnType switch
            {
                "int" or "long" or "double" or "float" or "decimal" => "return 0;",
                "bool" => "return false;",
                "string" => "return \"\";",
                _ when returnType.EndsWith("?") => "return null;",
                _ => "return default!;"
            };
        }

        return null;
    }

    private static string? DetectReturnType(string[] lines, int lineNumber)
    {
        // Walk backwards to find method signature
        for (int i = Math.Min(lineNumber - 1, lines.Length - 1); i >= 0; i--)
        {
            var match = MethodSigRegex().Match(lines[i]);
            if (match.Success)
                return match.Groups[1].Value;
        }
        return null;
    }

    private static string? ExtractGuardBlock(string[] lines, int lineNumber)
    {
        if (lineNumber <= 0 || lineNumber > lines.Length) return null;
        var line = lines[lineNumber - 1].Trim();

        // Single-line guard: "if (...) throw ...;"
        if (line.Contains("throw") && line.EndsWith(";"))
            return line;

        // Multi-line: find the closing brace
        int braceCount = 0;
        var block = new System.Text.StringBuilder();
        for (int i = lineNumber - 1; i < lines.Length && i < lineNumber + 5; i++)
        {
            var l = lines[i].Trim();
            block.AppendLine(l);
            braceCount += l.Count(c => c == '{') - l.Count(c => c == '}');
            if (braceCount <= 0 && i > lineNumber - 1)
                break;
        }
        return block.ToString().Trim();
    }

    private static string ReplaceFirst(string text, string search, string replacement)
    {
        var idx = text.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return text.Replace(search.Trim(), replacement.Trim());
        return string.Concat(text.AsSpan(0, idx), replacement, text.AsSpan(idx + search.Length));
    }

    // ── Regex patterns ──────────────────────────────────────────────

    [GeneratedRegex(@"[^!=<>](<=|>=|<|>|==|!=)\s*\w", RegexOptions.Compiled)]
    private static partial Regex BoundaryOpRegex();

    [GeneratedRegex(@"^\s*return\s+[^;]+;", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ReturnValueRegex();

    [GeneratedRegex(@"if\s*\([^)]+\)\s*throw\s+new\s+\w+", RegexOptions.Compiled)]
    private static partial Regex GuardClauseRegex();

    [GeneratedRegex(@"if\s*\([^)]{3,}\)", RegexOptions.Compiled)]
    private static partial Regex ConditionRegex();

    [GeneratedRegex(@"\.(First|Last|FirstOrDefault|LastOrDefault|Any|All|Min|Max|OrderBy|OrderByDescending)\s*\(", RegexOptions.Compiled)]
    private static partial Regex LinqMethodRegex();

    [GeneratedRegex(@"\w\s+(\+|-|\*|/)\s+\w", RegexOptions.Compiled)]
    private static partial Regex ArithmeticRegex();

    [GeneratedRegex(@"(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?(?:Task<)?(\w+\??)\>?\s+\w+\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodSigRegex();
}
