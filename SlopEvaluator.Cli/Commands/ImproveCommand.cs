using Microsoft.Extensions.Logging;
using SlopEvaluator.Orchestrator;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Scan, identify weak dimensions, mutate, suggest, and rescan.</summary>
public static class ImproveCommand
{
    public static async Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        // Parse: slop improve <path> [--targets N]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: slop improve <path> [--targets N]");
            return 1;
        }

        var path = Path.GetFullPath(args[1]);
        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Directory not found: {path}");
            return 1;
        }

        int maxTargets = 3;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--targets", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var t))
            {
                maxTargets = t;
                break;
            }
        }

        Console.WriteLine($"  Improve workflow: {path}");
        Console.WriteLine($"  Max targets:      {maxTargets}");
        Console.WriteLine();

        var orchestrator = new ImproveOrchestrator(loggerFactory);
        var result = await orchestrator.ImproveAsync(path, maxTargets: maxTargets);

        // Print summary
        Console.WriteLine();
        Console.WriteLine($"  \u2550\u2550\u2550 IMPROVE SUMMARY \u2550\u2550\u2550");
        Console.WriteLine($"  Before:            {result.BeforeScore:F3}");
        Console.WriteLine($"  After (projected): {result.AfterScore:F3}");
        Console.WriteLine($"  Delta:             {result.ScoreDelta:+0.000;-0.000;0.000}");
        Console.WriteLine();
        Console.WriteLine($"  Targets analyzed:  {result.Targets.Count}");
        Console.WriteLine($"  Mutants killed:    {result.TotalMutantsKilled}");
        Console.WriteLine($"  Mutants survived:  {result.TotalMutantsSurvived}");

        if (result.TotalMutantsSurvived > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Next steps: write tests to kill the surviving mutants.");
            Console.WriteLine("  Run `slop fix <file>` on surviving files for auto-generated test suggestions.");
        }

        return 0;
    }
}
