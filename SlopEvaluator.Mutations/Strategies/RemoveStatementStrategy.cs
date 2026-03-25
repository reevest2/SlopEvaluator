using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Strategies;

/// <summary>
/// Removes a statement from a method body by index.
/// Only targets statements that are safe to remove (expression statements,
/// if-blocks, loops — not variable declarations or returns).
/// </summary>
public sealed class RemoveStatementStrategy : IMutationStrategy
{
    public string Name => "remove-statement";

    public IReadOnlyList<MutationCandidate> FindCandidates(
        SyntaxTree tree, SyntaxNode root, string sourceFile)
    {
        var candidates = new List<MutationCandidate>();

        foreach (var (className, methodName, method) in NodeLocator.GetAllMethods(root))
        {
            if (method.Body is null) continue;

            for (int i = 0; i < method.Body.Statements.Count; i++)
            {
                var stmt = method.Body.Statements[i];
                if (!NodeLocator.IsSafeToRemove(stmt)) continue;

                var line = NodeLocator.GetLine(stmt);
                var stmtText = stmt.ToString().Trim();
                // Truncate long statements for readability
                var displayText = stmtText.Length > 60
                    ? stmtText[..57] + "..."
                    : stmtText;

                candidates.Add(new MutationCandidate
                {
                    Strategy = Name,
                    Description = $"Remove statement at line {line} in {className}.{methodName}",
                    OriginalCode = stmtText,
                    MutatedCode = "/* statement removed */",
                    RiskLevel = "high",
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

        var stmt = NodeLocator.GetStatement(method, spec.NodeIndex.Value);
        if (stmt is null) return null;

        var newRoot = root.RemoveNode(stmt, SyntaxRemoveOptions.KeepNoTrivia)!;
        return newRoot.ToFullString();
    }
}
