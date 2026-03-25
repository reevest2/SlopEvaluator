using System.Text.Json;
using System.Text.Json.Serialization;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Engine;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Commands;

/// <summary>
/// Shared helpers used by all command handlers.
/// Extracted from Program.cs to keep the entry point slim.
/// </summary>
internal static class CommandHelpers
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static MetricsCollector? CreateMetrics(CliOptions opts)
    {
        if (opts.NoMetrics)
            return null;
        return new MetricsCollector();
    }

    internal static async Task FlushMetrics(MetricsCollector? metrics, string path)
    {
        if (metrics is not null)
            await metrics.FlushAsync(path);
    }

    internal static HarnessConfig ApplyOverrides(HarnessConfig config, CliOptions opts)
    {
        if (opts.TestCommand is not null) config = config with { TestCommand = opts.TestCommand };
        if (opts.Timeout is not null) config = config with { TestTimeoutSeconds = int.Parse(opts.Timeout) };
        if (opts.Report is not null) config = config with { ReportPath = opts.Report };
        if (opts.RecommendedTests is not null) config = config with { RecommendedTestFile = opts.RecommendedTests };
        if (opts.Project is not null) config = config with { ProjectPath = opts.Project };

        return config;
    }

    internal static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"  \u2554\u2550{"".PadRight(title.Length + 2, '\u2550')}\u2550\u2557");
        Console.WriteLine($"  \u2551 {title}   \u2551");
        Console.WriteLine($"  \u255a\u2550{"".PadRight(title.Length + 2, '\u2550')}\u2550\u255d");
        Console.WriteLine();
    }
}
