using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Coverage + edge cases + mutations pipeline.</summary>
public static class QualityCommand
{
    public static Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("  quality: Coming soon — coverage + edge cases + mutations pipeline.");
        return Task.FromResult(0);
    }
}
