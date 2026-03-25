using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Scan, identify weak dimensions, mutate, suggest, and rescan.</summary>
public static class ImproveCommand
{
    public static Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("improve: Not yet implemented");
        return Task.FromResult(0);
    }
}
