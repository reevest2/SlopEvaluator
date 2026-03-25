using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Roslyn syntax walker that extracts method and class metrics from C# source.
/// </summary>
internal class MetricsWalker : CSharpSyntaxWalker
{
    private readonly string _filePath;

    public List<MethodMetrics> Methods { get; } = [];
    public List<ClassMetrics> Classes { get; } = [];
    public int XmlDocCount { get; private set; }
    public int PublicMemberCount { get; private set; }
    public int NullableAnnotatedParams { get; private set; }
    public int TotalNullableParams { get; private set; }

    public MetricsWalker(string filePath) : base(SyntaxWalkerDepth.Node)
    {
        _filePath = filePath;
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        int lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

        Classes.Add(new ClassMetrics
        {
            FilePath = _filePath,
            Name = node.Identifier.Text,
            LineCount = lineCount,
            MethodCount = node.Members.OfType<MethodDeclarationSyntax>().Count()
        });

        base.VisitClassDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        int lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

        Classes.Add(new ClassMetrics
        {
            FilePath = _filePath,
            Name = node.Identifier.Text,
            LineCount = lineCount,
            MethodCount = node.Members.OfType<MethodDeclarationSyntax>().Count()
        });

        base.VisitRecordDeclaration(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        AnalyzeMethod(node, node.Identifier.Text, node.ParameterList, node.Body, node.ExpressionBody);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        AnalyzeMethod(node, $"{node.Identifier.Text}.ctor", node.ParameterList, node.Body, node.ExpressionBody);
        base.VisitConstructorDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        CheckPublicMember(node.Modifiers, node);
        base.VisitPropertyDeclaration(node);
    }

    private void AnalyzeMethod(
        SyntaxNode node,
        string name,
        ParameterListSyntax? paramList,
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? expressionBody)
    {
        CheckPublicMember(node is MemberDeclarationSyntax m ? m.Modifiers : default, node);

        var lineSpan = node.GetLocation().GetLineSpan();
        int lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

        int cc = ComputeCyclomaticComplexity(node);
        int nesting = ComputeMaxNestingDepth(body ?? (SyntaxNode?)expressionBody ?? node);
        int paramCount = paramList?.Parameters.Count ?? 0;

        // Track nullable annotations
        if (paramList is not null)
        {
            foreach (var param in paramList.Parameters)
            {
                if (param.Type is not null && IsReferenceType(param.Type))
                {
                    TotalNullableParams++;
                    if (param.Type is NullableTypeSyntax)
                        NullableAnnotatedParams++;
                }
            }
        }

        bool hasTry = node.DescendantNodes().OfType<TryStatementSyntax>().Any();
        bool hasThrow = node.DescendantNodes().OfType<ThrowStatementSyntax>().Any()
            || node.DescendantNodes().OfType<ThrowExpressionSyntax>().Any();

        Methods.Add(new MethodMetrics
        {
            FilePath = _filePath,
            Name = name,
            CyclomaticComplexity = cc,
            LineCount = lineCount,
            NestingDepth = nesting,
            ParameterCount = paramCount,
            HasTryCatch = hasTry,
            HasThrow = hasThrow
        });
    }

    private void CheckPublicMember(SyntaxTokenList modifiers, SyntaxNode node)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            PublicMemberCount++;
            if (node.HasLeadingTrivia &&
                node.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                    || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)))
            {
                XmlDocCount++;
            }
        }
    }

    /// <summary>
    /// Cyclomatic complexity: start at 1, add 1 for each decision point.
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
                || node is ForStatementSyntax
                || node is ForEachStatementSyntax
                || node is WhileStatementSyntax
                || node is DoStatementSyntax
                || node is SwitchStatementSyntax
                || node is TryStatementSyntax
                || node is UsingStatementSyntax;

            int depth = isNesting ? currentDepth + 1 : currentDepth;
            if (depth > maxDepth) maxDepth = depth;

            foreach (var child in node.ChildNodes())
                Walk(child, depth);
        }

        Walk(root, 0);
        return maxDepth;
    }

    private static bool IsReferenceType(TypeSyntax type)
    {
        return type is IdentifierNameSyntax
            || type is QualifiedNameSyntax
            || type is GenericNameSyntax
            || type is NullableTypeSyntax
            || type is ArrayTypeSyntax;
    }
}
