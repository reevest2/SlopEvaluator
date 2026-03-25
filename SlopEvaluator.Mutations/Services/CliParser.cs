using Microsoft.Extensions.Logging;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Parsed CLI options. Every field mirrors the original args.Contains / GetNamedArg patterns
/// that were scattered throughout Program.cs.
/// </summary>
public sealed record CliOptions
{
    // Global
    public string Command { get; init; } = "";
    public bool ShowHelp { get; init; }
    public bool Verbose { get; init; }
    public bool NoMetrics { get; init; }

    // Positional
    public string? PositionalArg1 { get; init; }

    // Named args (mutate, coverage, etc.)
    public string? TestCommand { get; init; }
    public string? Source { get; init; }
    public string? Target { get; init; }
    public string? TestFile { get; init; }
    public string? TestProject { get; init; }
    public string? Report { get; init; }
    public string? ReportFile { get; init; }
    public string? Config { get; init; }
    public string? RecommendedTests { get; init; }
    public string? SaveConfig { get; init; }
    public string? History { get; init; }
    public string? Weights { get; init; }
    public string? Output { get; init; }
    public string? MutationReports { get; init; }
    public string? Project { get; init; }
    public string? Threshold { get; init; }
    public string? OutputFormat { get; init; }
    public string? Timeout { get; init; }
    public string? CoverageFile { get; init; }
    public string? Parallel { get; init; }
    public string? MaxRounds { get; init; }
    public string? MaxMutations { get; init; }
    public string? InputPrice { get; init; }
    public string? OutputPrice { get; init; }

    // Flags
    public bool DryRun { get; init; }
    public bool Watch { get; init; }
    public bool Merge { get; init; }
    public bool Roslyn { get; init; }
    public bool NoCoverage { get; init; }
    public bool NoEdgeCases { get; init; }
    public bool NoMutations { get; init; }
    public bool Smart { get; init; }
    public bool AgentProtocol { get; init; }

    // Auto / Agent source (value after --auto or --agent)
    public string? AutoSource { get; init; }
    public string? AgentSource { get; init; }

    // Agent context
    public string? ContextLevel { get; init; }

    // Scan command
    public string? MaxPerFile { get; init; }
    public string? MaxTotal { get; init; }

    /// <summary>The raw args array, preserved for any edge-case access.</summary>
    public string[] RawArgs { get; init; } = [];

    /// <summary>Optional logger factory for structured logging, injected after parsing.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }
}

public static class CliParser
{
    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return new CliOptions { ShowHelp = true, RawArgs = args };
        }

        return new CliOptions
        {
            Command = args[0].ToLowerInvariant(),
            ShowHelp = false,
            Verbose = Contains(args, "--verbose"),
            NoMetrics = Contains(args, "--no-metrics"),

            PositionalArg1 = GetArg(args, 1),

            TestCommand = GetNamed(args, "--test-command"),
            Source = GetNamed(args, "--source"),
            Target = GetNamed(args, "--target"),
            TestFile = GetNamed(args, "--test-file"),
            TestProject = GetNamed(args, "--test-project"),
            Report = GetNamed(args, "--report"),
            ReportFile = GetNamed(args, "--report-file"),
            Config = GetNamed(args, "--config"),
            RecommendedTests = GetNamed(args, "--recommended-tests"),
            SaveConfig = GetNamed(args, "--save-config"),
            History = GetNamed(args, "--history"),
            Weights = GetNamed(args, "--weights"),
            Output = GetNamed(args, "--output"),
            MutationReports = GetNamed(args, "--mutation-reports"),
            Project = GetNamed(args, "--project"),
            Threshold = GetNamed(args, "--threshold"),
            OutputFormat = GetNamed(args, "--output-format"),
            Timeout = GetNamed(args, "--timeout"),
            CoverageFile = GetNamed(args, "--coverage-file"),
            Parallel = GetNamed(args, "--parallel"),
            MaxRounds = GetNamed(args, "--max-rounds"),
            MaxMutations = GetNamed(args, "--max-mutations"),
            InputPrice = GetNamed(args, "--input-price"),
            OutputPrice = GetNamed(args, "--output-price"),

            DryRun = Contains(args, "--dry-run"),
            Watch = Contains(args, "--watch"),
            Merge = Contains(args, "--merge"),
            Roslyn = Contains(args, "--roslyn"),
            NoCoverage = Contains(args, "--no-coverage"),
            NoEdgeCases = Contains(args, "--no-edge-cases"),
            NoMutations = Contains(args, "--no-mutations"),
            Smart = Contains(args, "--smart"),
            AgentProtocol = Contains(args, "--agent-protocol"),

            AutoSource = GetNamed(args, "--auto"),
            AgentSource = GetNamed(args, "--agent"),

            ContextLevel = GetNamed(args, "--context-level"),
            MaxPerFile = GetNamed(args, "--max-per-file"),
            MaxTotal = GetNamed(args, "--max-total"),

            RawArgs = args
        };
    }

    // ── helpers (same logic as original Program.cs) ──

    private static string? GetArg(string[] args, int index) =>
        index < args.Length ? args[index] : null;

    private static string? GetNamed(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    private static bool Contains(string[] args, string flag) =>
        args.Contains(flag);
}
