using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SlopEvaluator.Mutations.Strategies;

namespace SlopEvaluator.Tests.Mutations.Strategies;

public class EmptyMethodBodyStrategyTests
{
    private readonly EmptyMethodBodyStrategy _strategy = new();

    [Fact]
    public void Name_ReturnsExpectedValue()
    {
        Assert.Equal("empty-method-body", _strategy.Name);
    }

    [Fact]
    public void FindCandidates_MethodWithMultipleStatements_FindsCandidate()
    {
        var source = @"
class MyClass
{
    public void DoWork()
    {
        var x = 1;
        Console.WriteLine(x);
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Single(candidates);
        Assert.Contains("MyClass.DoWork", candidates[0].Description);
        Assert.Equal("empty-method-body", candidates[0].Strategy);
        Assert.Equal("high", candidates[0].RiskLevel);
    }

    [Fact]
    public void FindCandidates_MethodWithSingleStatement_SkipsIt()
    {
        var source = @"
class MyClass
{
    public void Simple()
    {
        Console.WriteLine();
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Empty(candidates);
    }

    [Fact]
    public void FindCandidates_VoidMethod_UseCommentBody()
    {
        var source = @"
class MyClass
{
    public void DoWork()
    {
        var a = 1;
        var b = 2;
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Single(candidates);
        Assert.Contains("method body emptied", candidates[0].MutatedCode);
    }

    [Fact]
    public void FindCandidates_NonVoidMethod_UseThrowBody()
    {
        var source = @"
class MyClass
{
    public int Calculate()
    {
        var x = 1;
        return x + 1;
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Single(candidates);
        Assert.Contains("NotImplementedException", candidates[0].MutatedCode);
    }

    [Fact]
    public void FindCandidates_TaskMethod_UseCompletedTask()
    {
        var source = @"
using System.Threading.Tasks;
class MyClass
{
    public Task DoAsync()
    {
        var a = 1;
        return Task.CompletedTask;
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Single(candidates);
        Assert.Contains("Task.CompletedTask", candidates[0].MutatedCode);
    }
}

public class RemoveGuardClauseStrategyTests
{
    private readonly RemoveGuardClauseStrategy _strategy = new();

    [Fact]
    public void Name_ReturnsExpectedValue()
    {
        Assert.Equal("remove-guard", _strategy.Name);
    }

    [Fact]
    public void FindCandidates_MethodWithGuardClause_FindsCandidate()
    {
        var source = @"
using System;
class MyClass
{
    public void Process(string input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        Console.WriteLine(input);
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Single(candidates);
        Assert.Contains("remove-guard", candidates[0].Strategy);
        Assert.Contains("MyClass.Process", candidates[0].Description);
    }

    [Fact]
    public void FindCandidates_MethodWithoutGuards_ReturnsEmpty()
    {
        var source = @"
class MyClass
{
    public void Simple()
    {
        Console.WriteLine(""hello"");
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Empty(candidates);
    }

    [Fact]
    public void FindCandidates_MultipleGuards_FindsAll()
    {
        var source = @"
using System;
class MyClass
{
    public void Process(string a, string b)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));
        Console.WriteLine(a + b);
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Equal(2, candidates.Count);
    }
}

public class RemoveStatementStrategyTests
{
    private readonly RemoveStatementStrategy _strategy = new();

    [Fact]
    public void Name_ReturnsExpectedValue()
    {
        Assert.Equal("remove-statement", _strategy.Name);
    }

    [Fact]
    public void FindCandidates_ExpressionStatement_FindsCandidate()
    {
        var source = @"
class MyClass
{
    public void DoWork()
    {
        Console.WriteLine(""hello"");
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Single(candidates);
        Assert.Equal("remove-statement", candidates[0].Strategy);
    }

    [Fact]
    public void FindCandidates_ReturnStatement_SkipsIt()
    {
        var source = @"
class MyClass
{
    public int GetValue()
    {
        return 42;
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Empty(candidates);
    }

    [Fact]
    public void FindCandidates_VariableDeclaration_SkipsIt()
    {
        var source = @"
class MyClass
{
    public void DoWork()
    {
        var x = 42;
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Empty(candidates);
    }
}

public class SwapStatementsStrategyTests
{
    private readonly SwapStatementsStrategy _strategy = new();

    [Fact]
    public void Name_ReturnsExpectedValue()
    {
        Assert.Equal("swap-statements", _strategy.Name);
    }

    [Fact]
    public void FindCandidates_TwoSwappableStatements_FindsCandidate()
    {
        var source = @"
class MyClass
{
    public void DoWork()
    {
        Console.WriteLine(""first"");
        Console.WriteLine(""second"");
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Single(candidates);
        Assert.Equal("swap-statements", candidates[0].Strategy);
        Assert.Equal("medium", candidates[0].RiskLevel);
    }

    [Fact]
    public void FindCandidates_IdenticalStatements_SkipsThem()
    {
        var source = @"
class MyClass
{
    public void DoWork()
    {
        Console.WriteLine(""same"");
        Console.WriteLine(""same"");
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Empty(candidates);
    }

    [Fact]
    public void FindCandidates_DeclarationFollowedByExpression_SkipsIt()
    {
        var source = @"
class MyClass
{
    public void DoWork()
    {
        var x = 42;
        Console.WriteLine(x);
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        // var declaration is not swappable
        Assert.Empty(candidates);
    }

    [Fact]
    public void FindCandidates_SingleStatement_ReturnsEmpty()
    {
        var source = @"
class MyClass
{
    public void DoWork()
    {
        Console.WriteLine(""only"");
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var candidates = _strategy.FindCandidates(tree, root, "test.cs");

        Assert.Empty(candidates);
    }
}
