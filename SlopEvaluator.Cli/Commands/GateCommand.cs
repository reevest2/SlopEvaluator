using Microsoft.Extensions.Logging;
using SlopEvaluator.Orchestrator;

namespace SlopEvaluator.Cli.Commands;

/// <summary>CI quality gate with configurable threshold.</summary>
public static class GateCommand
{
    public static async Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        // Parse: slop gate <path> [--threshold 0.70]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: slop gate <path> [--threshold 0.70]");
            return 1;
        }

        var path = Path.GetFullPath(args[1]);
        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Directory not found: {path}");
            return 1;
        }

        double threshold = 0.70;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--threshold", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(args[i + 1], out var t))
            {
                threshold = t;
                break;
            }
        }

        Console.WriteLine($"  Quality gate: {path}");
        Console.WriteLine($"  Threshold:    {threshold:F2}");
        Console.WriteLine();

        var orchestrator = new GateOrchestrator(loggerFactory);
        var (passed, score) = await orchestrator.CheckAsync(path, threshold);

        var icon = passed ? "PASS" : "FAIL";
        var comparison = passed ? ">=" : "<";

        Console.WriteLine($"  Score:     {score:F3}");
        Console.WriteLine($"  Threshold: {threshold:F3}");
        Console.WriteLine($"  Result:    {icon} ({score:F3} {comparison} {threshold:F3})");

        return passed ? 0 : 1;
    }
}
