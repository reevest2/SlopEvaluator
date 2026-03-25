using Microsoft.Extensions.Logging;
using SlopEvaluator.Orchestrator;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Run mutation testing on a file.</summary>
public static class MutateCommand
{
    public static async Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        // Parse: slop mutate <file> [--auto] [--roslyn] [--max-mutations N] [--threshold N]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: slop mutate <file> [--test-command <cmd>] [--roslyn] [--max-mutations N] [--threshold N]");
            return 1;
        }

        var sourceFile = Path.GetFullPath(args[1]);
        if (!File.Exists(sourceFile))
        {
            Console.Error.WriteLine($"Source file not found: {sourceFile}");
            return 1;
        }

        // Parse options
        bool useRoslyn = HasFlag(args, "--roslyn");
        int maxMutations = GetIntOption(args, "--max-mutations", 15);
        double? threshold = GetDoubleOption(args, "--threshold");
        string? testCommand = GetOption(args, "--test-command");

        // Auto-detect test command if not provided
        if (testCommand is null)
        {
            var projectDir = Path.GetDirectoryName(sourceFile) ?? ".";
            // Walk up to find solution root
            var dir = new DirectoryInfo(projectDir);
            while (dir is not null)
            {
                if (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0)
                {
                    projectDir = dir.FullName;
                    break;
                }
                dir = dir.Parent;
            }

            testCommand = MutationOrchestrator.DetectTestCommand(projectDir);
            if (testCommand is null)
            {
                Console.Error.WriteLine("Could not auto-detect test command. Use --test-command <cmd>");
                return 1;
            }
        }

        Console.WriteLine($"  Source:     {sourceFile}");
        Console.WriteLine($"  Test cmd:  {testCommand}");
        Console.WriteLine($"  Roslyn:    {useRoslyn}");
        Console.WriteLine($"  Max:       {maxMutations}");
        Console.WriteLine();

        var orchestrator = new MutationOrchestrator(loggerFactory);
        var report = await orchestrator.MutateAsync(
            sourceFile, testCommand, maxMutations: maxMutations,
            useRoslyn: useRoslyn, threshold: threshold);

        if (threshold.HasValue)
        {
            return report.MutationScore >= threshold.Value ? 0 : 1;
        }

        return 0;
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static int GetIntOption(string[] args, string name, int defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var value))
                return value;
        }
        return defaultValue;
    }

    private static double? GetDoubleOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(args[i + 1], out var value))
                return value;
        }
        return null;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
