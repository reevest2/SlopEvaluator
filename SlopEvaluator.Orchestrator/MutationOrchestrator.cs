using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Mutations.Engine;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Models;

namespace SlopEvaluator.Orchestrator;

/// <summary>
/// Wraps MutationEngine + RoslynMutationGenerator for the mutate command.
/// Auto-generates mutations from a source file and runs the mutation engine.
/// </summary>
public sealed class MutationOrchestrator
{
    private readonly ILoggerFactory _loggerFactory;

    public MutationOrchestrator(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Auto-generate mutations from a source file and run the mutation engine.
    /// </summary>
    /// <param name="sourceFile">Path to the .cs file to mutate.</param>
    /// <param name="testCommand">The dotnet test command to run.</param>
    /// <param name="projectPath">Optional path to the solution or test project.</param>
    /// <param name="maxMutations">Maximum number of mutations to generate.</param>
    /// <param name="useRoslyn">Whether to use Roslyn-based mutation generation (default: true).</param>
    /// <param name="threshold">Optional mutation score threshold for pass/fail.</param>
    /// <returns>The mutation report.</returns>
    public async Task<MutationReport> MutateAsync(
        string sourceFile,
        string testCommand,
        string? projectPath = null,
        int maxMutations = 15,
        bool useRoslyn = true,
        double? threshold = null)
    {
        sourceFile = Path.GetFullPath(sourceFile);
        projectPath ??= "";

        var logger = _loggerFactory.CreateLogger<MutationEngine>();

        // Generate mutations
        HarnessConfig config;
        if (useRoslyn)
        {
            var generator = new RoslynMutationGenerator(sourceFile);
            config = generator.Generate(testCommand, projectPath, maxMutations);
        }
        else
        {
            var generator = new MutationGenerator(sourceFile);
            config = generator.Generate(testCommand, projectPath, maxMutations);
        }

        if (config.Mutations.Count == 0)
        {
            Console.WriteLine("  No mutable patterns found in source file.");
            return new MutationReport
            {
                SourceFile = sourceFile,
                Target = Path.GetFileName(sourceFile),
                RunDate = DateTime.UtcNow,
                TotalDuration = TimeSpan.Zero,
                BaselineDuration = TimeSpan.Zero,
                BaselineTestCount = 0,
                Results = []
            };
        }

        // Run mutation engine
        var engine = new MutationEngine(config, useRoslyn: useRoslyn, logger: logger);
        var report = await engine.RunAsync();

        if (threshold.HasValue && report.MutationScore < threshold.Value)
        {
            Console.WriteLine($"\n  Mutation score {report.MutationScore:F1}% is below threshold {threshold.Value:F1}%");
        }

        return report;
    }

    /// <summary>
    /// Auto-detect the test command for a project by finding test .csproj files.
    /// </summary>
    public static string? DetectTestCommand(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);

        // Look for test projects
        var testProjects = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/")
                     && !f.Replace('\\', '/').Contains("/bin/"))
            .Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return name.Contains("Test", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Spec", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (testProjects.Count == 0)
            return null;

        // If there's a solution file, use it
        var slnFiles = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly))
            .ToList();

        if (slnFiles.Count > 0)
            return $"dotnet test \"{slnFiles[0]}\" --no-restore";

        // Otherwise use the first test project
        return $"dotnet test \"{testProjects[0]}\" --no-restore";
    }
}
