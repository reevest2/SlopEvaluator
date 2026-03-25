using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Show health score trend over time.</summary>
public static class HistoryCommand
{
    public static Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("  history: Coming soon — show health score trend over time.");
        return Task.FromResult(0);
    }
}
