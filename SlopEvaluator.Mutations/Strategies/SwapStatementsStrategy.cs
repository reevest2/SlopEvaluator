using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Strategies;

/// <summary>
/// Swaps two adjacent statements in a method body.
/// Only swaps pairs where both statements are safe to reorder
/// (expression statements, assignments, method calls — not declarations or returns).
/// </summary>
public sealed class SwapStatementsStrategy : IMutationStrategy
{
    public string Name => "swap-statements";

    public IReadOnlyList<MutationCandidate> FindCandidates(
        SyntaxTree tree, SyntaxNode root, string sourceFile)
    {
        var candidates = new List<MutationCandidate>();

        foreach (var (className, methodName, method) in NodeLocator.GetAllMethods(root))
        {
            if (method.Body is null) continue;

            var statements = method.Body.Statements;
            for (int i = 0; i < statements.Count - 1; i++)
            {
                var first = statements[i];
                var second = statements[i + 1];

                // Both must be safe to reorder (expression statements, if blocks, etc.)
                if (!IsSwappable(first) || !IsSwappable(second)) continue;

                // Skip if they're the same text (swap would be a no-op)
                var firstText = first.ToString().Trim();
                var secondText = second.ToString().Trim();
                if (firstText == secondText) continue;

                var line = NodeLocator.GetLine(first);
                var displayFirst = firstText.Length > 40 ? firstText[..37] + "..." : firstText;
                var displaySecond = secondText.Length > 40 ? secondText[..37] + "..." : secondText;

                candidates.Add(new MutationCandidate
                {
                    Strategy = Name,
                    Description = $"Swap statements at lines {line}-{NodeLocator.GetLine(second)} in {className}.{methodName}",
                    OriginalCode = $"{displayFirst}\n{displaySecond}",
                    MutatedCode = $"{displaySecond}\n{displayFirst}",
                    RiskLevel = "medium",
                    LineNumber = line,
                    TargetMethod = $"{className}.{methodName}",
                    NodeIndex = i,
                    Parameters = new Dictionary<string, string>
                    {
                        ["statementIndex"] = i.ToString()
                    }
                });
            }
        }

        return candidates;
    }

    public string? ApplyStructural(SyntaxTree tree, SyntaxNode root, MutationSpec spec)
    {
        if (spec.Strategy != Name || spec.TargetMethod is null || spec.NodeIndex is null)
            return null;

        var method = NodeLocator.FindMethod(root, spec.TargetMethod);
        if (method?.Body is null) return null;

        var idx = spec.NodeIndex.Value;
        var statements = method.Body.Statements;
        if (idx < 0 || idx >= statements.Count - 1) return null;

        var first = statements[idx];
        var second = statements[idx + 1];

        // Swap: replace both nodes in one pass
        var newRoot = root.ReplaceNodes(
            new[] { first, second },
            (original, _) =>
            {
                if (original == first) return second.WithTriviaFrom(first);
                if (original == second) return first.WithTriviaFrom(second);
                return original;
            });

        return newRoot.ToFullString();
    }

    private static bool IsSwappable(StatementSyntax statement) => statement switch
    {
        ExpressionStatementSyntax => true,
        IfStatementSyntax ifs when !NodeLocator.IsGuardClause(ifs) => true,
        _ => false
    };
}
