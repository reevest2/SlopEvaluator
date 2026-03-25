using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Full 14-dimension health scan command.</summary>
public static class ScanCommand
{
    public static Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("scan: Not yet implemented");
        return Task.FromResult(0);
    }
}
