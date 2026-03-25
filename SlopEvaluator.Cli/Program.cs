using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(
                args.Contains("--verbose") ? LogLevel.Debug : LogLevel.Information));

        if (args.Length == 0)
            return PrintUsage();

        return args[0].ToLowerInvariant() switch
        {
            "scan" => await Commands.ScanCommand.RunAsync(args, loggerFactory),
            "mutate" => await Commands.MutateCommand.RunAsync(args, loggerFactory),
            "fix" => await Commands.FixCommand.RunAsync(args, loggerFactory),
            "improve" => await Commands.ImproveCommand.RunAsync(args, loggerFactory),
            "quality" => await Commands.QualityCommand.RunAsync(args, loggerFactory),
            "history" => await Commands.HistoryCommand.RunAsync(args, loggerFactory),
            "compare" => await Commands.CompareCommand.RunAsync(args, loggerFactory),
            "gate" => await Commands.GateCommand.RunAsync(args, loggerFactory),
            _ => PrintUsage()
        };
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            SlopEvaluator — Scan → Improve → Verify for any .NET codebase

            Usage: slop <command> [options]

            Commands:
              scan <path>              Full 14-dimension health scan
              mutate <file> [options]  Run mutation testing on a file
              fix <file>               Auto-generate killing tests for survivors
              improve <path>           Scan → identify weak → mutate → suggest → rescan
              quality <file>           Coverage + edge cases + mutations pipeline
              history <name>           Show health score trend
              compare <path1> <path2>  Compare two codebases
              gate <path> --threshold  CI quality gate
            """);
        return 0;
    }
}
