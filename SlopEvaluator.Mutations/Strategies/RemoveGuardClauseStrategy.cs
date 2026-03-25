using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Strategies;

/// <summary>
/// Cleanly removes a guard clause (if-throw pattern) from a method body
/// using Roslyn AST node removal, instead of text comment-out.
/// </summary>
public sealed class RemoveGuardClauseStrategy : IMutationStrategy
{
    public string Name => "remove-guard";

    public IReadOnlyList<MutationCandidate> FindCandidates(
        SyntaxTree tree, SyntaxNode root, string sourceFile)
    {
        var candidates = new List<MutationCandidate>();

        foreach (var (className, methodName, method) in NodeLocator.GetAllMethods(root))
        {
            var guards = NodeLocator.FindGuardClauses(method);
            for (int i = 0; i < guards.Count; i++)
            {
                var guard = guards[i];
                var line = NodeLocator.GetLine(guard);
                var condition = guard.Condition.ToString().Trim();
                var guardText = guard.ToString().Trim();
                var firstLine = LineHelpers.SplitLines(guardText)[0].Trim();

                candidates.Add(new MutationCandidate
                {
                    Strategy = Name,
                    Description = $"Remove guard clause '{condition}' at line {line} in {className}.{methodName}",
                    OriginalCode = firstLine,
                    MutatedCode = "/* guard removed */",
                    RiskLevel = "high",
                    LineNumber = line,
                    TargetMethod = $"{className}.{methodName}",
                    NodeIndex = i,
                    Parameters = new Dictionary<string, string>
                    {
                        ["guardIndex"] = i.ToString()
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
        if (method is null) return null;

        var guards = NodeLocator.FindGuardClauses(method);
        var idx = spec.NodeIndex.Value;
        if (idx < 0 || idx >= guards.Count) return null;

        var guard = guards[idx];
        var newRoot = root.RemoveNode(guard, SyntaxRemoveOptions.KeepNoTrivia)!;
        return newRoot.ToFullString();
    }
}
