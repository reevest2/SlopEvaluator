using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Orchestrator;

/// <summary>
/// Given a Codebase health result, identifies the weakest files to target for mutation testing.
/// </summary>
public sealed class TargetSelector
{
    /// <summary>
    /// Select the weakest files in a project for mutation testing.
    /// </summary>
    /// <param name="health">The codebase health result from a scan.</param>
    /// <param name="projectPath">Path to the project root.</param>
    /// <param name="count">Number of targets to return.</param>
    /// <returns>Ranked list of mutation targets.</returns>
    public List<MutationTarget> SelectTargets(Codebase health, string projectPath, int count = 3)
    {
        projectPath = Path.GetFullPath(projectPath);

        // Find all .cs source files (exclude obj, bin, test files, generated)
        var sourceFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var normalized = f.Replace('\\', '/');
                return !normalized.Contains("/obj/")
                    && !normalized.Contains("/bin/")
                    && !normalized.Contains("/node_modules/")
                    && !normalized.Contains(".g.cs")
                    && !normalized.Contains(".designer.cs", StringComparison.OrdinalIgnoreCase)
                    && !normalized.Contains("GlobalUsings")
                    && !normalized.Contains("AssemblyInfo");
            })
            .ToList();

        // Exclude test files — they're not mutation targets
        sourceFiles = sourceFiles
            .Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var dir = Path.GetDirectoryName(f)?.Replace('\\', '/') ?? "";
                return !name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
                    && !dir.Contains("/Test", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        // Get project-level weakness signals
        double testingWeakness = 1.0 - health.Testing.Score;
        double mutationWeakness = 1.0 - health.Testing.MutationScore;
        double coverageWeakness = 1.0 - health.Testing.BranchCoverage;
        double qualityWeakness = 1.0 - health.Quality.Score;

        // Score each file based on complexity heuristics + project-level signals
        var scored = new List<MutationTarget>();
        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            var lineCount = content.Split('\n').Length;

            // Skip trivially small files
            if (lineCount < 10) continue;

            var reason = new List<string>();
            double weakness = 0.0;

            // File complexity heuristics
            var complexity = EstimateComplexity(content);
            weakness += complexity * 0.3;
            if (complexity > 0.6) reason.Add($"high complexity ({complexity:F2})");

            // Larger files have more mutation surface
            var sizeFactor = Math.Min(1.0, lineCount / 300.0);
            weakness += sizeFactor * 0.2;
            if (lineCount > 200) reason.Add($"{lineCount} lines");

            // Project-level signals boost all files proportionally
            weakness += testingWeakness * 0.2;
            weakness += mutationWeakness * 0.15;
            weakness += qualityWeakness * 0.15;

            if (testingWeakness > 0.5) reason.Add("weak testing dimension");
            if (mutationWeakness > 0.5) reason.Add("low mutation score");

            // Bonus: files with scoring logic or business rules are high-value
            if (content.Contains("Score", StringComparison.Ordinal)
                || content.Contains("Calculate", StringComparison.Ordinal)
                || content.Contains("Compute", StringComparison.Ordinal))
            {
                weakness += 0.1;
                reason.Add("contains scoring/calculation logic");
            }

            scored.Add(new MutationTarget(
                FilePath: file,
                Reason: reason.Count > 0 ? string.Join("; ", reason) : "general mutation candidate",
                WeaknessScore: Math.Min(1.0, weakness)));
        }

        return scored
            .OrderByDescending(t => t.WeaknessScore)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Estimate code complexity from source content using simple heuristics.
    /// Returns 0.0 (trivial) to 1.0 (very complex).
    /// </summary>
    private static double EstimateComplexity(string content)
    {
        int branches = 0;
        branches += CountOccurrences(content, " if ");
        branches += CountOccurrences(content, " if(");
        branches += CountOccurrences(content, " else ");
        branches += CountOccurrences(content, " switch ");
        branches += CountOccurrences(content, " case ");
        branches += CountOccurrences(content, " for ");
        branches += CountOccurrences(content, " foreach ");
        branches += CountOccurrences(content, " while ");
        branches += CountOccurrences(content, " catch ");
        branches += CountOccurrences(content, " ? ");
        branches += CountOccurrences(content, " ?? ");

        var lines = content.Split('\n').Length;
        if (lines == 0) return 0;

        // Cyclomatic complexity density
        var density = (double)branches / lines;
        return Math.Min(1.0, density * 10.0);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}

/// <summary>
/// A file identified as a weak spot for mutation testing.
/// </summary>
/// <param name="FilePath">Full path to the source file.</param>
/// <param name="Reason">Human-readable explanation of why this file was selected.</param>
/// <param name="WeaknessScore">Score from 0.0 (strongest) to 1.0 (weakest).</param>
public sealed record MutationTarget(string FilePath, string Reason, double WeaknessScore);
