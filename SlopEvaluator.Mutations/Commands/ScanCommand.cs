using SlopEvaluator.Mutations.Models;
using static SlopEvaluator.Mutations.Commands.CommandHelpers;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Engine;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Commands;

internal static class ScanCommand
{
    internal static async Task<int> RunAsync(CliOptions opts)
    {
        var directory = opts.PositionalArg1 ?? ".";
        if (opts.TestCommand is null)
        {
            Console.Error.WriteLine("Usage: SlopEvaluator scan <directory> --test-command <cmd>");
            return 1;
        }

        var projectPath = opts.Project ?? "";
        var maxPerFile = int.TryParse(opts.MaxPerFile, out var mpf) ? mpf : 5;
        var maxTotal = int.TryParse(opts.MaxTotal, out var mt) ? mt : 30;
        var threshold = double.TryParse(opts.Threshold, out var t) ? t : (double?)null;

        PrintHeader("DIRECTORY SCAN");
        Console.WriteLine($"  Directory:   {Path.GetFullPath(directory)}");
        Console.WriteLine($"  Test cmd:    {opts.TestCommand}");
        Console.WriteLine($"  Max/file:    {maxPerFile}");
        Console.WriteLine($"  Max total:   {maxTotal}");
        Console.WriteLine();

        var scanner = new DirectoryScanner();
        var configs = scanner.Scan(directory, opts.TestCommand, projectPath, maxPerFile, maxTotal);

        if (configs.Count == 0)
        {
            Console.WriteLine("  No mutable patterns found in any source files.");
            return 0;
        }

        var allResults = new List<MutationResultEntry>();
        foreach (var config in configs)
        {
            var relPath = Path.GetRelativePath(Path.GetFullPath(directory), config.SourceFile);
            Console.WriteLine();
            Console.WriteLine($"  -- {relPath} ({config.Mutations.Count} mutations) --");

            var engine = new MutationEngine(config, Console.WriteLine, useRoslyn: true);
            var report = await engine.RunAsync();
            allResults.AddRange(report.Results);

            Console.WriteLine($"  Score: {report.MutationScore:F1}% ({report.Killed} killed, {report.Survived} survived)");
        }

        var totalKilled = allResults.Count(r => r.Outcome == MutationOutcome.Killed);
        var totalSurvived = allResults.Count(r => r.Outcome == MutationOutcome.Survived);
        var totalValid = totalKilled + totalSurvived;
        var overallScore = totalValid == 0 ? 0 : (double)totalKilled / totalValid * 100;

        Console.WriteLine();
        PrintHeader("SCAN SUMMARY");
        Console.WriteLine($"  Files scanned:  {configs.Count}");
        Console.WriteLine($"  Total mutations: {allResults.Count}");
        Console.WriteLine($"  Overall score:   {overallScore:F1}%");
        Console.WriteLine($"  Killed:          {totalKilled}");
        Console.WriteLine($"  Survived:        {totalSurvived}");

        if (threshold.HasValue && overallScore < threshold.Value)
        {
            Console.Error.WriteLine($"  FAILED: Score {overallScore:F1}% < threshold {threshold.Value}%");
            return 1;
        }

        return 0;
    }
}
