using Microsoft.Extensions.Logging;
using SlopEvaluator.Health.Models;
using SlopEvaluator.Orchestrator;

namespace SlopEvaluator.Cli.Commands;

/// <summary>Full 14-dimension health scan command.</summary>
public static class ScanCommand
{
    public static async Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        // Parse: slop scan <path> [name]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: slop scan <path> [name]");
            return 1;
        }

        var path = Path.GetFullPath(args[1]);
        var name = args.Length > 2 ? args[2] : null;

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Directory not found: {path}");
            return 1;
        }

        Console.WriteLine($"  Running full health scan: {path}");
        Console.WriteLine($"  This will run ALL collectors — may take a minute...\n");

        var orchestrator = new ScanOrchestrator(loggerFactory);
        var (codebase, delta) = await orchestrator.ScanAsync(path, name);

        PrintHealthDashboard(codebase);
        PrintDelta(delta);

        return 0;
    }

    internal static void PrintHealthDashboard(Codebase codebase)
    {
        var name = codebase.Name;
        Console.WriteLine();
        Console.WriteLine($"  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
        Console.WriteLine($"  \u2551  {name,-20} Health: {codebase.Score:F3}  \u2551");
        Console.WriteLine($"  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
        Console.WriteLine();
        Console.WriteLine($"  {"Dimension",-22} {"Score",6}  {"Visual",-20}");
        Console.WriteLine($"  {new string('\u2500', 22)} {new string('\u2500', 6)}  {new string('\u2500', 20)}");
        Console.WriteLine($"  Code Quality         {codebase.Quality.Score:F3}  {Bar(codebase.Quality.Score)}");
        Console.WriteLine($"  Testing              {codebase.Testing.Score:F3}  {Bar(codebase.Testing.Score)}");
        Console.WriteLine($"  Dependencies         {codebase.Dependencies.Score:F3}  {Bar(codebase.Dependencies.Score)}");
        Console.WriteLine($"  Security             {codebase.Security.Score:F3}  {Bar(codebase.Security.Score)}");
        Console.WriteLine($"  Observability        {codebase.Observability.Score:F3}  {Bar(codebase.Observability.Score)}");
        Console.WriteLine($"  CI/CD Pipeline       {codebase.Pipeline.Score:F3}  {Bar(codebase.Pipeline.Score)}");
        Console.WriteLine($"  Documentation        {codebase.Documentation.Score:F3}  {Bar(codebase.Documentation.Score)}");
        Console.WriteLine($"  Dev Experience       {codebase.DevEx.Score:F3}  {Bar(codebase.DevEx.Score)}");
        Console.WriteLine($"  Performance          {codebase.Performance.Score:F3}  {Bar(codebase.Performance.Score)}");
        Console.WriteLine($"  Requirements         {codebase.Requirements.Score:F3}  {Bar(codebase.Requirements.Score)}");
        Console.WriteLine($"  Team Process         {codebase.Process.Score:F3}  {Bar(codebase.Process.Score)}");
        Console.WriteLine($"  AI Quality           {codebase.AIQuality.Score:F3}  {Bar(codebase.AIQuality.Score)}");
        Console.WriteLine($"  Architecture         {codebase.Architecture.Score:F3}  {Bar(codebase.Architecture.Score)}");
        Console.WriteLine($"  Structure            {codebase.Structure.Score:F3}  {Bar(codebase.Structure.Score)}");
        Console.WriteLine();
        Console.WriteLine($"  Stack: {codebase.Stack.TargetFramework} | {codebase.Stack.SdkType} | nullable={codebase.Stack.NullableEnabled}");
        Console.WriteLine($"  Architecture: {codebase.Architecture.PrimaryPattern} [{string.Join(", ", codebase.Architecture.Patterns)}]");
    }

    private static void PrintDelta(SnapshotDelta? delta)
    {
        if (delta is null) return;

        var arrow = delta.OverallScoreDelta > 0 ? "+" : delta.OverallScoreDelta < 0 ? "" : "=";
        Console.WriteLine($"\n  Delta from previous: {arrow}{delta.OverallScoreDelta:F3} ({delta.TimeBetween.TotalHours:F0}h ago)");

        if (delta.Improvements.Count > 0)
        {
            Console.WriteLine($"  Improved:");
            foreach (var imp in delta.Improvements.Take(5))
                Console.WriteLine($"    {imp}");
        }
        if (delta.Regressions.Count > 0)
        {
            Console.WriteLine($"  Regressed:");
            foreach (var reg in delta.Regressions.Take(5))
                Console.WriteLine($"    {reg}");
        }
    }

    internal static string Bar(double value) => new string('\u2588', (int)(value * 20)).PadRight(20, '\u2591');
}
