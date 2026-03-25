using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Helpers for locating specific Roslyn syntax nodes by method name, index, etc.
/// Used by structural mutation strategies to find their target nodes.
/// </summary>
public static class NodeLocator
{
    /// <summary>
    /// Find a method declaration by name. Supports "ClassName.MethodName" or just "MethodName".
    /// Returns the first match.
    /// </summary>
    public static MethodDeclarationSyntax? FindMethod(SyntaxNode root, string targetMethod)
    {
        var parts = targetMethod.Split('.');
        var methodName = parts[^1];
        var className = parts.Length > 1 ? parts[^2] : null;

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Identifier.Text != methodName)
                continue;

            if (className is null)
                return method;

            // Check containing class name
            var containingClass = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (containingClass?.Identifier.Text == className)
                return method;
        }

        return null;
    }

    /// <summary>
    /// Get the Nth statement in a method body (0-based index).
    /// </summary>
    public static StatementSyntax? GetStatement(MethodDeclarationSyntax method, int index)
    {
        if (method.Body is null)
            return null;

        var statements = method.Body.Statements;
        return index >= 0 && index < statements.Count ? statements[index] : null;
    }

    /// <summary>
    /// Find guard clauses (if-throw patterns) in a method.
    /// </summary>
    public static IReadOnlyList<IfStatementSyntax> FindGuardClauses(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
            return [];

        return method.Body.Statements
            .OfType<IfStatementSyntax>()
            .Where(IsGuardClause)
            .ToList();
    }

    /// <summary>
    /// Get all methods in a syntax tree with their containing class name.
    /// Returns (className, methodName, method) tuples.
    /// </summary>
    public static IReadOnlyList<(string ClassName, string MethodName, MethodDeclarationSyntax Method)>
        GetAllMethods(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(m => m.Body is not null && m.Body.Statements.Count > 0)
            .Select(m =>
            {
                var className = m.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()?.Identifier.Text ?? "Unknown";
                return (className, m.Identifier.Text, m);
            })
            .ToList();
    }

    /// <summary>
    /// Get the line number (1-based) of a syntax node.
    /// </summary>
    public static int GetLine(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>
    /// Check if an if statement is a guard clause (if-throw or if-block-with-single-throw).
    /// </summary>
    public static bool IsGuardClause(IfStatementSyntax ifStatement)
    {
        if (ifStatement.Statement is ThrowStatementSyntax)
            return true;

        if (ifStatement.Statement is BlockSyntax block
            && block.Statements.Count == 1
            && block.Statements[0] is ThrowStatementSyntax)
            return true;

        return false;
    }

    /// <summary>
    /// Check if a statement is safe to remove (won't break compilation).
    /// Skips: variable declarations, return statements, yield statements.
    /// </summary>
    public static bool IsSafeToRemove(StatementSyntax statement)
    {
        return statement switch
        {
            // Variable declarations might be referenced later
            LocalDeclarationStatementSyntax => false,
            // Return statements are required for non-void methods
            ReturnStatementSyntax => false,
            // Yield statements are part of iterator pattern
            YieldStatementSyntax => false,
            // Expression statements (method calls, assignments) are generally safe
            ExpressionStatementSyntax => true,
            // If statements (including guards) are safe to remove
            IfStatementSyntax => true,
            // For/foreach/while loops are safe to remove
            ForStatementSyntax => true,
            ForEachStatementSyntax => true,
            WhileStatementSyntax => true,
            // Try blocks are safe to remove
            TryStatementSyntax => true,
            // Using statements are safe to remove (resource leak mutation)
            UsingStatementSyntax => true,
            // Lock statements are safe to remove (concurrency mutation)
            LockStatementSyntax => true,
            // Throw statements are safe to remove (they might be guards)
            ThrowStatementSyntax => true,
            // Default: not safe
            _ => false
        };
    }
}
