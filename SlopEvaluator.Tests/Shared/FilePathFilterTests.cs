using SlopEvaluator.Shared.Roslyn;

namespace SlopEvaluator.Tests.Shared;

public class FilePathFilterTests
{
    private const string Root = "/repo";

    [Fact]
    public void ShouldInclude_RegularSourceFile_ReturnsTrue()
    {
        Assert.True(FilePathFilter.ShouldInclude(Root, "/repo/src/MyClass.cs"));
    }

    [Fact]
    public void ShouldInclude_ObjDirectory_ReturnsFalse()
    {
        Assert.False(FilePathFilter.ShouldInclude(Root, "/repo/src/obj/Debug/file.cs"));
    }

    [Fact]
    public void ShouldInclude_BinDirectory_ReturnsFalse()
    {
        Assert.False(FilePathFilter.ShouldInclude(Root, "/repo/src/bin/Release/file.dll"));
    }

    [Fact]
    public void ShouldInclude_WorktreeDirectory_ReturnsFalse()
    {
        Assert.False(FilePathFilter.ShouldInclude(Root, "/repo/.claude/worktrees/my-branch/file.cs"));
    }

    [Fact]
    public void ShouldInclude_BenchmarksDirectory_ReturnsFalse()
    {
        Assert.False(FilePathFilter.ShouldInclude(Root, "/repo/benchmarks/Bench.cs"));
    }

    [Fact]
    public void ShouldInclude_ObjAtRoot_ReturnsFalse()
    {
        Assert.False(FilePathFilter.ShouldInclude(Root, "/repo/obj/file.cs"));
    }

    [Fact]
    public void ShouldInclude_BinAtRoot_ReturnsFalse()
    {
        Assert.False(FilePathFilter.ShouldInclude(Root, "/repo/bin/file.cs"));
    }

    [Theory]
    [InlineData("/repo/Controllers/HomeController.cs")]
    [InlineData("/repo/Models/User.cs")]
    [InlineData("/repo/Tests/UnitTests.cs")]
    public void ShouldInclude_VariousSourcePaths_ReturnsTrue(string filePath)
    {
        Assert.True(FilePathFilter.ShouldInclude(Root, filePath));
    }

    [Fact]
    public void ShouldInclude_HandlesBackslashPaths()
    {
        // The method normalizes backslashes to forward slashes
        Assert.False(FilePathFilter.ShouldInclude("C:\\repo", "C:\\repo\\obj\\Debug\\file.cs"));
    }
}
