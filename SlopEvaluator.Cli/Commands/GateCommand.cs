using Microsoft.Extensions.Logging;

namespace SlopEvaluator.Cli.Commands;

/// <summary>CI quality gate with configurable threshold.</summary>
public static class GateCommand
{
    public static Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("gate: Not yet implemented");
        return Task.FromResult(0);
    }
}
