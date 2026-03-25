using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;

namespace SlopEvaluator.Mutations.Analysis;

/// <summary>
/// Analyzes C# source files to extract cyclomatic complexity, nesting depth,
/// and line counts per method. Used for complexity-aware mutation targeting.
/// </summary>
public static class ComplexityAnalyzer
{
    /// <summary>
    /// Analyze a source file and return method complexity metrics,
    /// ordered by cyclomatic complexity descending (most complex first).
    /// </summary>
    public static List<MethodComplexity> AnalyzeFile(string sourceFile)
    {
        var source = File.ReadAllText(sourceFile);
        return AnalyzeSource(source);
    }

    /// <summary>
    /// Analyze source code string and return method complexity metrics,
    /// ordered by cyclomatic complexity descending.
    /// </summary>
    public static List<MethodComplexity> AnalyzeSource(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var results = new List<MethodComplexity>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var className = method.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault()?.Identifier.Text ?? "Unknown";

            var lineSpan = method.GetLocation().GetLineSpan();
            int lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;
            int lineNumber = lineSpan.StartLinePosition.Line + 1;

            results.Add(new MethodComplexity
            {
                ClassName = className,
                MethodName = method.Identifier.Text,
                CyclomaticComplexity = ComputeCyclomaticComplexity(method),
                LineCount = lineCount,
                NestingDepth = ComputeMaxNestingDepth(method.Body ?? (SyntaxNode?)method.ExpressionBody ?? method),
                LineNumber = lineNumber
            });
        }

        return results.OrderByDescending(m => m.CyclomaticComplexity).ToList();
    }

    /// <summary>
    /// Calculate mutation budget per method based on complexity.
    /// Higher-complexity methods get more mutations.
    /// </summary>
    public static Dictionary<string, int> CalculateBudget(
        List<MethodComplexity> methods, int maxTotal = 15)
    {
        if (methods.Count == 0)
            return new Dictionary<string, int>();

        var budget = new Dictionary<string, int>();

        // Assign raw budgets based on complexity tiers
        foreach (var m in methods)
        {
            var key = $"{m.ClassName}.{m.MethodName}";
            int raw = m.CyclomaticComplexity switch
            {
                <= 5 => 3 + Math.Min(m.CyclomaticComplexity - 1, 2),  // 3-5
                <= 15 => 6 + Math.Min(m.CyclomaticComplexity - 6, 4), // 6-10
                _ => 10 + Math.Min(m.CyclomaticComplexity - 16, 5)    // 10-15
            };
            budget[key] = raw;
        }

        // Scale down if total exceeds maxTotal
        int total = budget.Values.Sum();
        if (total > maxTotal)
        {
            double scale = (double)maxTotal / total;
            var keys = budget.Keys.ToList();
            foreach (var key in keys)
                budget[key] = Math.Max(1, (int)Math.Round(budget[key] * scale));

            // Trim excess from highest budgets if rounding pushed over
            while (budget.Values.Sum() > maxTotal)
            {
                var maxKey = budget.OrderByDescending(kv => kv.Value).First().Key;
                budget[maxKey]--;
            }
        }

        return budget;
    }

    /// <summary>
    /// Cyclomatic complexity: start at 1, +1 for each decision point.
    /// </summary>
    internal static int ComputeCyclomaticComplexity(SyntaxNode node)
    {
        int cc = 1;

        foreach (var descendant in node.DescendantNodes())
        {
            switch (descendant)
            {
                case IfStatementSyntax:
                case ElseClauseSyntax:
                case ConditionalExpressionSyntax:       // ? :
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                case SwitchExpressionArmSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case CatchClauseSyntax:
                    cc++;
                    break;
                case BinaryExpressionSyntax binary when
                    binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                    binary.IsKind(SyntaxKind.LogicalOrExpression) ||
                    binary.IsKind(SyntaxKind.CoalesceExpression):
                    cc++;
                    break;
            }
        }

        return cc;
    }

    /// <summary>
    /// Max nesting depth of control structures.
    /// </summary>
    internal static int ComputeMaxNestingDepth(SyntaxNode root)
    {
        int maxDepth = 0;

        void Walk(SyntaxNode node, int currentDepth)
        {
            bool isNesting = node is IfStatementSyntax
                or ForStatementSyntax
                or ForEachStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax
                or SwitchStatementSyntax
                or TryStatementSyntax
                or UsingStatementSyntax;

            int depth = isNesting ? currentDepth + 1 : currentDepth;
            if (depth > maxDepth) maxDepth = depth;

            foreach (var child in node.ChildNodes())
                Walk(child, depth);
        }

        Walk(root, 0);
        return maxDepth;
    }
}

/// <summary>
/// Complexity metrics for a single method.
/// </summary>
public sealed class MethodComplexity
{
    public required string ClassName { get; init; }
    public required string MethodName { get; init; }
    public required int CyclomaticComplexity { get; init; }
    public required int LineCount { get; init; }
    public required int NestingDepth { get; init; }
    public required int LineNumber { get; init; }
}
