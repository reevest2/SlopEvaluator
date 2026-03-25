using System.Text.Json;
using Microsoft.Extensions.Logging;
using SlopEvaluator.Mutations.Models;
using static SlopEvaluator.Mutations.Commands.CommandHelpers;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Engine;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Commands;

internal static class MutateCommand
{
    internal static async Task<int> RunAsync(CliOptions opts)
    {
        // Agent mode: autonomous mutation-test-fix loop
        if (opts.AgentSource is not null)
        {
            if (opts.TestCommand is null)
            {
                Console.Error.WriteLine("Usage: mutation-harness mutate --agent <source.cs> --test-command <cmd>");
                return 1;
            }
            var maxRounds = int.TryParse(opts.MaxRounds, out var mr) ? mr : 5;
            var maxMut = int.TryParse(opts.MaxMutations, out var mm) ? mm : 15;

            var agentMetrics = CreateMetrics(opts);
            var agentLogger = opts.LoggerFactory?.CreateLogger<AgentLoop>();
            var agent = new AgentLoop(opts.AgentSource, opts.TestCommand,
                projectPath: opts.Project,
                maxRounds: maxRounds, maxMutations: maxMut, useRoslyn: opts.Roslyn,
                contextLevel: opts.ContextLevel ?? "standard",
                metrics: agentMetrics, logger: agentLogger);

            int result;
            if (opts.AgentProtocol)
                result = await agent.RunProtocolAsync();
            else
                result = await agent.RunLoopAsync();

            if (agentMetrics is not null)
                await FlushMetrics(agentMetrics, Path.ChangeExtension(opts.AgentSource, ".agent-metrics.jsonl"));
            return result;
        }

        // Auto-generate mode
        if (opts.AutoSource is not null)
            return await RunAutoAsync(opts);

        // Merge mode: combine multiple configs
        if (opts.Merge)
        {
            var configPaths = opts.RawArgs.Where(a => a.EndsWith(".json") && File.Exists(a)).ToList();
            if (configPaths.Count < 2)
            {
                Console.Error.WriteLine("Usage: SlopEvaluator mutate --merge config1.json config2.json [...]");
                return 1;
            }
            var configs = configPaths.Select(p => ReportSerializer.LoadConfig(p)).ToArray();
            var merged = ConfigMerger.Merge(configs);
            var tempConfig = Path.Combine(Path.GetTempPath(), $"merged-{Guid.NewGuid():N}.json");
            var json = JsonSerializer.Serialize(merged, JsonOptions);
            await File.WriteAllTextAsync(tempConfig, json);
            var mergedArgs = new[] { "mutate", tempConfig }
                .Concat(opts.RawArgs.Where(a => a.StartsWith("--") && a != "--merge"))
                .ToArray();
            opts = CliParser.Parse(mergedArgs);
            // Fall through to normal mutate with merged config
        }

        var configPath = opts.PositionalArg1;
        if (configPath is null || !File.Exists(configPath))
        {
            Console.Error.WriteLine(configPath is null
                ? "Usage: SlopEvaluator mutate <config.json>"
                : $"Config file not found: {configPath}");
            return 1;
        }

        var config = ReportSerializer.LoadConfig(configPath);
        config = ApplyOverrides(config, opts);

        if (config.Mutations.Count == 0)
        {
            Console.Error.WriteLine("No mutations defined in config. Nothing to test.");
            return 1;
        }

        var parallelism = int.TryParse(opts.Parallel, out var p) ? p : 1;

        // Pre-flight health check
        var healthLogger = opts.LoggerFactory?.CreateLogger("HealthCheck");
        var health = HealthCheck.Check(config.SourceFile, config.TestCommand,
            Path.GetDirectoryName(config.ReportPath), healthLogger);
        if (!health.IsHealthy)
        {
            foreach (var err in health.Errors)
                Console.Error.WriteLine($"  Health check error: {err}");
            return 2;
        }
        foreach (var warn in health.Warnings)
            Console.WriteLine($"  Health check warning: {warn}");

        PrintHeader("MUTATION TEST HARNESS");
        Console.WriteLine($"  Config:      {configPath}");
        Console.WriteLine($"  Source:      {config.SourceFile}");
        Console.WriteLine($"  Test cmd:    {config.TestCommand}");
        Console.WriteLine($"  Mutations:   {config.Mutations.Count}");
        Console.WriteLine($"  Timeout:     {config.TestTimeoutSeconds}s per run");
        if (parallelism > 1)
            Console.WriteLine($"  Parallel:    {parallelism} workers");
        if (config.RecommendedTestFile is not null)
            Console.WriteLine($"  Rec. tests:  {config.RecommendedTestFile}");

        // Watch mode -- re-run survivors on file changes
        if (opts.Watch)
            return await RunWatchAsync(config, parallelism, opts);

        if (opts.DryRun)
        {
            Console.WriteLine();
            Console.WriteLine("  DRY RUN \u2014 planned mutations:");
            Console.WriteLine();
            foreach (var m in config.Mutations)
            {
                var risk = m.RiskLevel switch { "high" => "\ud83d\udd34", "medium" => "\ud83d\udfe1", _ => "\ud83d\udfe2" };
                Console.WriteLine($"  {risk} {m.Id} [{m.Strategy}] {m.Description}");
                Console.WriteLine($"       {m.OriginalCode}");
                Console.WriteLine($"     \u2192 {m.MutatedCode}");
                Console.WriteLine();
            }
            return 0;
        }

        var metrics = CreateMetrics(opts);
        var mutationLogger = opts.LoggerFactory?.CreateLogger<MutationEngine>();
        var engine = new MutationEngine(config, metrics: metrics, parallelism: parallelism, useRoslyn: opts.Roslyn, logger: mutationLogger);
        var report = await engine.RunAsync();

        var jsonPath = config.ReportPath;
        var htmlPath = Path.ChangeExtension(config.ReportPath, ".html");
        await ReportSerializer.WriteJsonAsync(report, jsonPath);
        await ReportSerializer.WriteHtmlAsync(report, htmlPath);

        var metricsPath = Path.ChangeExtension(config.ReportPath, ".metrics.jsonl");
        await FlushMetrics(metrics, metricsPath);

        // Record to trend history
        var historyPath = opts.History ?? ".mutation-history.jsonl";
        await TrendAnalyzer.RecordRunAsync(report, historyPath);

        Console.WriteLine();
        Console.WriteLine($"  Reports: {jsonPath}, {htmlPath}");
        if (metrics is not null)
            Console.WriteLine($"  Metrics: {metricsPath}");

        // CI output format
        var outputFormat = opts.OutputFormat ?? "text";
        if (outputFormat == "json")
        {
            var summary = new
            {
                source = report.SourceFile,
                target = report.Target,
                mutationScore = Math.Round(report.MutationScore, 1),
                killed = report.Killed,
                survived = report.Survived,
                compileErrors = report.CompileErrors,
                total = report.Results.Count,
                survivors = report.Results
                    .Where(r => r.Outcome == MutationOutcome.Survived)
                    .Select(r => new { r.Id, r.Strategy, r.Description, r.RiskLevel })
                    .ToArray()
            };
            Console.WriteLine(JsonSerializer.Serialize(summary, JsonOptions));
        }

        // Threshold gate
        if (opts.Threshold is not null && double.TryParse(opts.Threshold, out var threshold))
        {
            if (report.MutationScore < threshold)
            {
                Console.Error.WriteLine(
                    $"  \u274c QUALITY GATE FAILED: mutation score {report.MutationScore:F1}% < threshold {threshold}%");
                return 1;
            }
            Console.WriteLine(
                $"  \u2705 QUALITY GATE PASSED: mutation score {report.MutationScore:F1}% >= threshold {threshold}%");
            return 0;
        }

        return report.Survived > 0 ? 1 : 0;
    }

    // ── auto-generate ──

    private static async Task<int> RunAutoAsync(CliOptions opts)
    {
        var sourceFile = opts.AutoSource!;
        if (!File.Exists(sourceFile))
        {
            Console.Error.WriteLine($"Source file not found: {sourceFile}");
            return 1;
        }

        if (opts.TestCommand is null)
        {
            Console.Error.WriteLine("Usage: SlopEvaluator mutate --auto <source.cs> --test-command <cmd>");
            return 1;
        }

        var projectPath = opts.Project ?? "";
        var maxMutations = int.TryParse(opts.MaxMutations, out var m) ? m : 15;

        PrintHeader("AUTO-GENERATE MUTATIONS");
        Console.WriteLine($"  Source:      {sourceFile}");
        Console.WriteLine($"  Test cmd:    {opts.TestCommand}");
        Console.WriteLine($"  Max:         {maxMutations}");
        Console.WriteLine();

        HarnessConfig config;
        if (opts.Roslyn)
        {
            TrendReport? trendHistory = null;
            if (opts.Smart)
            {
                var historyPath = Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(sourceFile)) ?? ".",
                    ".mutation-history.jsonl");
                trendHistory = TrendAnalyzer.Analyze(historyPath);
                if (trendHistory.Entries.Count > 0)
                    Console.WriteLine($"  Smart mode: loaded {trendHistory.Entries.Count} history entries");
            }
            var roslynGen = new RoslynMutationGenerator(sourceFile);
            config = roslynGen.Generate(opts.TestCommand, projectPath, maxMutations, trendHistory);
        }
        else
        {
            var regexGen = new MutationGenerator(sourceFile);
            config = regexGen.Generate(opts.TestCommand, projectPath, maxMutations);
        }
        config = ApplyOverrides(config, opts);

        if (config.Mutations.Count == 0)
        {
            Console.WriteLine("  No mutable patterns found in source file.");
            return 0;
        }

        // Save generated config for reuse
        if (opts.SaveConfig is not null)
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(opts.SaveConfig, json);
            Console.WriteLine($"  Config saved: {opts.SaveConfig}");
            Console.WriteLine();
        }

        if (opts.DryRun)
        {
            Console.WriteLine("  DRY RUN \u2014 generated mutations:");
            Console.WriteLine();
            foreach (var mut in config.Mutations)
            {
                var risk = mut.RiskLevel switch { "high" => "\ud83d\udd34", "medium" => "\ud83d\udfe1", _ => "\ud83d\udfe2" };
                Console.WriteLine($"  {risk} {mut.Id} [{mut.Strategy}] {mut.Description}");
                Console.WriteLine($"       \u2212 {mut.OriginalCode?.ReplaceLineEndings(" ").Trim()[..Math.Min(80, mut.OriginalCode.Length)]}");
                Console.WriteLine($"       + {mut.MutatedCode?.ReplaceLineEndings(" ").Trim()[..Math.Min(80, mut.MutatedCode.Length)]}");
                Console.WriteLine();
            }
            return 0;
        }

        // Run the mutations
        var metrics = CreateMetrics(opts);
        var autoLogger = opts.LoggerFactory?.CreateLogger<MutationEngine>();
        var engine = new MutationEngine(config, metrics: metrics, logger: autoLogger);
        var report = await engine.RunAsync();

        var jsonPath = config.ReportPath;
        var htmlPath = Path.ChangeExtension(config.ReportPath, ".html");
        await ReportSerializer.WriteJsonAsync(report, jsonPath);
        await ReportSerializer.WriteHtmlAsync(report, htmlPath);

        var metricsPath = Path.ChangeExtension(config.ReportPath, ".metrics.jsonl");
        await FlushMetrics(metrics, metricsPath);

        Console.WriteLine();
        Console.WriteLine($"  Reports: {jsonPath}, {htmlPath}");

        if (opts.Threshold is not null && double.TryParse(opts.Threshold, out var threshold))
        {
            if (report.MutationScore < threshold)
            {
                Console.Error.WriteLine(
                    $"  \u274c QUALITY GATE FAILED: mutation score {report.MutationScore:F1}% < threshold {threshold}%");
                return 1;
            }
            Console.WriteLine(
                $"  \u2705 QUALITY GATE PASSED: mutation score {report.MutationScore:F1}% >= threshold {threshold}%");
            return 0;
        }

        return report.Survived > 0 ? 1 : 0;
    }

    // ── watch mode ──

    private static async Task<int> RunWatchAsync(HarnessConfig config, int parallelism, CliOptions opts)
    {
        Console.WriteLine();
        Console.WriteLine("  \U0001f440 WATCH MODE \u2014 monitoring for file changes...");
        Console.WriteLine("  Press Ctrl+C to stop.");
        Console.WriteLine();

        var watchDir = Path.GetDirectoryName(Path.GetFullPath(config.SourceFile)) ?? ".";
        if (config.ProjectPath is { Length: > 0 })
            watchDir = Path.GetDirectoryName(Path.GetFullPath(config.ProjectPath)) ?? watchDir;

        MutationReport? lastReport = null;
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        Console.WriteLine("  Running initial full mutation test...");
        var metrics = CreateMetrics(opts);
        var engine = new MutationEngine(config, metrics: metrics, parallelism: parallelism);
        lastReport = await engine.RunAsync(cts.Token);
        var survivors = lastReport.Results.Where(r => r.Outcome == MutationOutcome.Survived).ToList();
        Console.WriteLine($"\n  Initial: {lastReport.Killed} killed, {survivors.Count} survivors");

        if (survivors.Count == 0)
        {
            Console.WriteLine("  \u2705 All mutants killed! Nothing to watch for.");
            return 0;
        }

        using var watcher = new FileSystemWatcher(watchDir)
        {
            Filter = "*.cs",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        var changeDetected = new TaskCompletionSource<bool>();
        var lastChangeTime = DateTime.MinValue;
        string lastChangePath = "";
        void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Suppress duplicate events for the same file within 500ms
            var now = DateTime.UtcNow;
            if (e.FullPath == lastChangePath && (now - lastChangeTime).TotalMilliseconds < 500)
                return;
            lastChangeTime = now;
            lastChangePath = e.FullPath;
            changeDetected.TrySetResult(true);
        }
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.EnableRaisingEvents = true;

        while (!cts.Token.IsCancellationRequested && survivors.Count > 0)
        {
            Console.WriteLine($"\n  Watching {watchDir} for changes ({survivors.Count} survivors remaining)...");

            var completedTask = await Task.WhenAny(
                changeDetected.Task,
                Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => false));

            if (cts.Token.IsCancellationRequested) break;

            await Task.Delay(500, cts.Token).ContinueWith(_ => { });
            changeDetected = new TaskCompletionSource<bool>();

            Console.WriteLine("\n  \U0001f504 Change detected! Re-testing survivors...");

            var survivorMutations = config.Mutations
                .Where(m => survivors.Any(s => s.Id == m.Id))
                .ToList();

            var watchConfig = config with { Mutations = survivorMutations };
            var watchEngine = new MutationEngine(watchConfig, parallelism: parallelism);

            try
            {
                var watchReport = await watchEngine.RunAsync(cts.Token);
                var nowKilled = watchReport.Results
                    .Where(r => r.Outcome == MutationOutcome.Killed)
                    .Select(r => r.Id).ToList();

                if (nowKilled.Count > 0)
                {
                    Console.WriteLine($"\n  \U0001f5e1\ufe0f Newly killed: {string.Join(", ", nowKilled)}");
                    survivors = survivors.Where(s => !nowKilled.Contains(s.Id)).ToList();
                }

                var remaining = watchReport.Results
                    .Where(r => r.Outcome == MutationOutcome.Survived).ToList();
                Console.WriteLine($"  {survivors.Count} survivors remaining");

                if (survivors.Count == 0)
                {
                    Console.WriteLine("\n  \u2705 ALL MUTANTS KILLED! Watch mode complete.");
                    return 0;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Baseline"))
            {
                Console.WriteLine($"  \u274c Baseline failed \u2014 tests are broken. Fix them and save again.");
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Console.WriteLine("\n  Watch mode stopped.");
        return survivors.Count > 0 ? 1 : 0;
    }
}
