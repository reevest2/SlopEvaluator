using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Run mutation testing on a file.</summary>
public static class MutateCommand
{
    public static Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("mutate: Not yet implemented");
        return Task.FromResult(0);
    }
}
