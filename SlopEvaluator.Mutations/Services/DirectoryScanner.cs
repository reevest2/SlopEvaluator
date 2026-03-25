using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;
using SlopEvaluator.Mutations.Generators;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Scans all .cs files in a directory, generates mutation candidates per file,
/// and produces a list of HarnessConfig objects (one per source file).
/// Excludes test files, generated files, and build output directories.
/// </summary>
public sealed class DirectoryScanner
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "obj", "bin", "node_modules", ".git", ".vs", ".claude"
    };

    private readonly Action<string> _log;

    public DirectoryScanner(Action<string>? log = null)
    {
        _log = log ?? Console.WriteLine;
    }

    /// <summary>
    /// Scan a directory for .cs files and generate mutation configs.
    /// Returns one HarnessConfig per source file that has mutable patterns.
    /// </summary>
    public List<HarnessConfig> Scan(
        string directory,
        string testCommand,
        string? projectPath = null,
        int maxPerFile = 5,
        int maxTotal = 30)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        var csFiles = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f))
            .OrderBy(f => f)
            .ToList();

        _log($"  Found {csFiles.Count} C# source files to scan");

        var configs = new List<HarnessConfig>();
        var totalMutations = 0;

        foreach (var file in csFiles)
        {
            if (totalMutations >= maxTotal) break;

            try
            {
                var remaining = maxTotal - totalMutations;
                var limit = Math.Min(maxPerFile, remaining);

                var gen = new RoslynMutationGenerator(file, _ => { });
                var config = gen.Generate(testCommand, projectPath, limit);

                if (config.Mutations.Count > 0)
                {
                    configs.Add(config);
                    totalMutations += config.Mutations.Count;
                    var relPath = Path.GetRelativePath(directory, file);
                    _log($"  {relPath}: {config.Mutations.Count} mutations");
                }
            }
            catch (Exception ex)
            {
                var relPath = Path.GetRelativePath(directory, file);
                _log($"  {relPath}: skipped ({ex.Message})");
            }
        }

        _log($"  Total: {totalMutations} mutations across {configs.Count} files");
        return configs;
    }

    private static bool IsExcluded(string filePath)
    {
        // Exclude test files
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Test", StringComparison.OrdinalIgnoreCase))
            return true;

        // Exclude generated files
        if (fileName.EndsWith(".g") || fileName.EndsWith(".designer", StringComparison.OrdinalIgnoreCase))
            return true;

        // Exclude files in excluded directories
        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => ExcludedDirs.Contains(p));
    }
}
