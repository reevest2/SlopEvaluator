using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Auto-generate killing tests for mutation survivors.</summary>
public static class FixCommand
{
    public static Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("fix: Not yet implemented");
        return Task.FromResult(0);
    }
}
