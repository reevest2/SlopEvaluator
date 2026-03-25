namespace SlopEvaluator.Shared.Roslyn;

/// <summary>Shared file path filter for excluding build artifacts and worktrees.</summary>
public static class FilePathFilter
{
    public static bool ShouldInclude(string rootPath, string filePath)
    {
        var rel = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
            && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
            && !rel.Contains(".claude/worktrees/")
            && !rel.Contains("benchmarks/");
    }
}
