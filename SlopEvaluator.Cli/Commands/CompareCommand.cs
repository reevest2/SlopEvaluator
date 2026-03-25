using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Compare two codebases side by side.</summary>
public static class CompareCommand
{
    public static Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("compare: Not yet implemented");
        return Task.FromResult(0);
    }
}
