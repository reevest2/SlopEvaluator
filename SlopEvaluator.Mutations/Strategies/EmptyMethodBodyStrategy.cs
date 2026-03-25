using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Strategies;

/// <summary>
/// Replaces a method body with a single throw NotImplementedException()
/// (for non-void methods) or an empty body (for void methods).
/// Only targets methods with 2+ statements to avoid trivial mutations.
/// </summary>
public sealed class EmptyMethodBodyStrategy : IMutationStrategy
{
    public string Name => "empty-method-body";

    public IReadOnlyList<MutationCandidate> FindCandidates(
        SyntaxTree tree, SyntaxNode root, string sourceFile)
    {
        var candidates = new List<MutationCandidate>();

        foreach (var (className, methodName, method) in NodeLocator.GetAllMethods(root))
        {
            if (method.Body is null || method.Body.Statements.Count < 2) continue;

            var line = NodeLocator.GetLine(method);
            var returnType = method.ReturnType.ToString().Trim();
            var replacement = GetReplacementBody(returnType);

            candidates.Add(new MutationCandidate
            {
                Strategy = Name,
                Description = $"Empty method body of {className}.{methodName} ({method.Body.Statements.Count} statements)",
                OriginalCode = $"{method.ReturnType} {method.Identifier}({method.ParameterList})",
                MutatedCode = $"{{ {replacement} }}",
                RiskLevel = "high",
                LineNumber = line,
                TargetMethod = $"{className}.{methodName}",
                NodeIndex = 0
            });
        }

        return candidates;
    }

    public string? ApplyStructural(SyntaxTree tree, SyntaxNode root, MutationSpec spec)
    {
        if (spec.Strategy != Name || spec.TargetMethod is null) return null;

        var method = NodeLocator.FindMethod(root, spec.TargetMethod);
        if (method?.Body is null) return null;

        var returnType = method.ReturnType.ToString().Trim();
        var replacementCode = GetReplacementBody(returnType);
        var newBody = SyntaxFactory.ParseStatement($"{{ {replacementCode} }}");

        // Build a new block with the single replacement statement
        var statements = SyntaxFactory.ParseStatement(replacementCode);
        var newBlock = SyntaxFactory.Block(statements)
            .WithOpenBraceToken(method.Body.OpenBraceToken)
            .WithCloseBraceToken(method.Body.CloseBraceToken);

        var newRoot = root.ReplaceNode(method.Body, newBlock);
        return newRoot.ToFullString();
    }

    private static string GetReplacementBody(string returnType) => returnType switch
    {
        "void" => "/* method body emptied */",
        "Task" => "return Task.CompletedTask;",
        _ when returnType.StartsWith("Task<") => "throw new NotImplementedException();",
        _ => "throw new NotImplementedException();"
    };
}
