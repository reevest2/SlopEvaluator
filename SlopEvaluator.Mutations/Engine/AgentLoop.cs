using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Engine;

/// <summary>
/// Autonomous mutation-test-fix loop designed to work with AI agents.
/// Each round: generate mutations → run → output survivors as structured JSON →
/// wait for agent to write killing tests → re-run survivors → repeat.
/// </summary>
public sealed class AgentLoop
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _sourceFile;
    private readonly string _testCommand;
    private readonly string? _projectPath;
    private readonly int _maxRounds;
    private readonly int _maxMutations;
    private readonly bool _useRoslyn;
    private readonly string _contextLevel;
    private readonly Action<string> _log;
    private readonly MetricsCollector? _metrics;
    private readonly ILogger? _logger;
    private readonly string _sessionId = Guid.NewGuid().ToString("N")[..12];

    public AgentLoop(
        string sourceFile,
        string testCommand,
        string? projectPath = null,
        int maxRounds = 5,
        int maxMutations = 15,
        bool useRoslyn = true,
        string contextLevel = "standard",
        Action<string>? log = null,
        MetricsCollector? metrics = null,
        ILogger? logger = null)
    {
        _sourceFile = Path.GetFullPath(sourceFile);
        _testCommand = testCommand;
        _projectPath = projectPath;
        _maxRounds = maxRounds;
        _maxMutations = maxMutations;
        _useRoslyn = useRoslyn;
        _contextLevel = contextLevel;
        _log = log ?? Console.WriteLine;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Runs one round: generate → execute → return structured survivor report.
    /// Returns the report as JSON string for the AI agent to consume.
    /// </summary>
    public async Task<AgentRoundResult> RunRoundAsync(
        List<string>? onlySurvivorIds = null,
        CancellationToken ct = default)
    {
        // Generate mutations
        HarnessConfig config;
        if (_useRoslyn)
        {
            var gen = new RoslynMutationGenerator(_sourceFile, _log);
            config = gen.Generate(_testCommand, _projectPath, _maxMutations);
        }
        else
        {
            var gen = new MutationGenerator(_sourceFile, _log);
            config = gen.Generate(_testCommand, _projectPath, _maxMutations);
        }

        // If re-running, filter to only previous survivors
        if (onlySurvivorIds is { Count: > 0 })
        {
            config = config with
            {
                Mutations = config.Mutations
                    .Where(m => onlySurvivorIds.Contains(m.Id))
                    .ToList()
            };
        }

        if (config.Mutations.Count == 0)
        {
            return new AgentRoundResult
            {
                Score = 100,
                TotalMutations = 0,
                Killed = 0,
                Survivors = [],
                AllKilled = true,
                Message = "No mutations to test."
            };
        }

        // Run mutations
        var engine = new MutationEngine(config, _log, useRoslyn: _useRoslyn);
        var report = await engine.RunAsync(ct);

        // Build structured survivor report for the AI agent
        var sourceLines = File.Exists(_sourceFile)
            ? File.ReadAllLines(_sourceFile) : [];
        var syntaxContext = _contextLevel != "minimal" && File.Exists(_sourceFile)
            ? BuildSyntaxContext(_sourceFile) : null;

        var survivors = report.Results
            .Where(r => r.Outcome == MutationOutcome.Survived)
            .Select(r => BuildSurvivorInfo(r, sourceLines, syntaxContext))
            .ToList();

        // Find test file hints
        var testFileHints = FindTestFileHints();

        return new AgentRoundResult
        {
            Score = report.MutationScore,
            TotalMutations = report.Results.Count,
            Killed = report.Killed,
            Survivors = survivors,
            AllKilled = survivors.Count == 0,
            TestFileHints = testFileHints,
            Message = survivors.Count == 0
                ? "All mutants killed! Test suite is robust."
                : $"{survivors.Count} mutants survived. Write tests to kill them."
        };
    }

    /// <summary>
    /// Runs the full autonomous loop: generate → test → report → (agent writes tests) → repeat.
    /// Outputs structured JSON after each round for the AI agent to consume.
    /// </summary>
    public async Task<int> RunLoopAsync(CancellationToken ct = default)
    {
        _log("");
        _log("  \U0001f916 AGENT MODE — autonomous mutation-test loop");
        _log($"  Source:     {_sourceFile}");
        _log($"  Max rounds: {_maxRounds}");
        _log($"  Engine:     {(_useRoslyn ? "Roslyn AST" : "Regex")}");
        _log("");

        List<string>? survivorIds = null;
        var historyPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(_sourceFile)) ?? ".",
            ".mutation-history.jsonl");

        for (int round = 1; round <= _maxRounds; round++)
        {
            _log($"  ── Round {round}/{_maxRounds} ──────────────────────────────");
            _logger?.LogInformation("Agent round {Round}/{MaxRounds} starting", round, _maxRounds);

            var result = await RunRoundAsync(survivorIds, ct);

            // Record to history with token data for efficiency analysis
            var tokenSummary = _metrics?.TokenCounter.GetSummary();
            var roundReport = new MutationReport
            {
                SourceFile = _sourceFile,
                Target = Path.GetFileNameWithoutExtension(_sourceFile),
                RunDate = DateTime.UtcNow,
                TotalDuration = TimeSpan.Zero,
                BaselineDuration = TimeSpan.Zero,
                BaselineTestCount = 0,
                Results = []
            };
            for (int i = 0; i < result.Killed; i++)
                roundReport.Results.Add(new MutationResultEntry
                {
                    Id = $"K{i + 1:D2}", Strategy = "", Description = "",
                    RiskLevel = "medium",
                    Outcome = MutationOutcome.Killed, Duration = TimeSpan.Zero
                });
            foreach (var s in result.Survivors)
                roundReport.Results.Add(new MutationResultEntry
                {
                    Id = s.Id, Strategy = s.Strategy, Description = s.Description,
                    RiskLevel = s.RiskLevel,
                    Outcome = MutationOutcome.Survived, Duration = TimeSpan.Zero
                });

            await TrendAnalyzer.RecordRunAsync(roundReport, historyPath,
                tokenUsage: tokenSummary, round: round, sessionId: _sessionId);

            // Output structured JSON for the AI agent
            _log("");
            _log("  === AGENT_OUTPUT_BEGIN ===");
            var json = JsonSerializer.Serialize(result, JsonOptions);
            _log(json);
            _log("  === AGENT_OUTPUT_END ===");
            _log("");

            _logger?.LogInformation("Agent round {Round}/{MaxRounds} complete: {Killed} killed, {Survivors} survivors",
                round, _maxRounds, result.Killed, result.Survivors.Count);

            if (result.AllKilled)
            {
                _log($"  \u2705 All mutants killed in round {round}!");
                return 0;
            }

            _log($"  \U0001f6e1\ufe0f {result.Survivors.Count} survivors remain.");
            _log("  Waiting for AI agent to write killing tests...");
            _log("  (Save test file to continue, or Ctrl+C to stop)");

            // Wait for file changes (agent writes tests)
            var testDir = Path.GetDirectoryName(Path.GetFullPath(_sourceFile)) ?? ".";
            if (!await WaitForFileChange(testDir, ct))
                break;

            survivorIds = result.Survivors.Select(s => s.Id).ToList();
            _log("  Change detected — re-running survivors...");
        }

        _log($"  Agent loop completed after {_maxRounds} rounds.");
        return 1;
    }

    private SurvivorInfo BuildSurvivorInfo(
        MutationResultEntry r, string[] sourceLines, SyntaxContext? syntaxContext)
    {
        var info = new SurvivorInfo
        {
            Id = r.Id,
            Strategy = r.Strategy,
            Description = r.Description,
            RiskLevel = r.RiskLevel,
            OriginalCode = r.OriginalCode ?? "",
            MutatedCode = r.MutatedCode ?? "",
            LineNumber = r.LineNumberHint,
            SuggestedTestApproach = SuggestTestApproach(r)
        };

        if (_contextLevel == "minimal" || r.LineNumberHint is null)
            return info;

        // Add surrounding code context (5 lines before/after)
        var lineIdx = r.LineNumberHint.Value - 1;
        if (lineIdx >= 0 && lineIdx < sourceLines.Length)
        {
            var start = Math.Max(0, lineIdx - 5);
            var end = Math.Min(sourceLines.Length, lineIdx + 6);
            info = info with
            {
                SurroundingCode = string.Join('\n',
                    sourceLines[start..end].Select((l, i) =>
                        $"{start + i + 1,4}: {l}"))
            };
        }

        // Add method/class context from syntax tree
        if (syntaxContext is not null)
        {
            var method = syntaxContext.Methods
                .FirstOrDefault(m => m.StartLine <= r.LineNumberHint.Value
                                  && m.EndLine >= r.LineNumberHint.Value);
            if (method is not null)
            {
                info = info with
                {
                    MethodSignature = method.Signature,
                    ClassName = method.ClassName
                };
            }
        }

        // In full mode, add test names that ran
        if (_contextLevel == "full" && r.FailedTestNames is not null)
        {
            info = info with
            {
                RelevantTestNames = r.FailedTestNames.Split(',', StringSplitOptions.TrimEntries).ToList()
            };
        }

        return info;
    }

    private static SyntaxContext? BuildSyntaxContext(string sourceFile)
    {
        try
        {
            var source = File.ReadAllText(sourceFile);
            var tree = CSharpSyntaxTree.ParseText(source, path: sourceFile);
            var root = tree.GetRoot();

            var methods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Select(m =>
                {
                    var className = m.Ancestors()
                        .OfType<TypeDeclarationSyntax>()
                        .FirstOrDefault()?.Identifier.Text ?? "Unknown";
                    var span = m.GetLocation().GetLineSpan();
                    return new MethodContext
                    {
                        ClassName = className,
                        MethodName = m.Identifier.Text,
                        Signature = $"{m.Modifiers} {m.ReturnType} {m.Identifier}{m.ParameterList}".Trim(),
                        StartLine = span.StartLinePosition.Line + 1,
                        EndLine = span.EndLinePosition.Line + 1
                    };
                })
                .ToList();

            return new SyntaxContext { Methods = methods };
        }
        catch
        {
            return null;
        }
    }

    private List<string> FindTestFileHints()
    {
        var hints = new List<string>();
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(_sourceFile));
        if (sourceDir is null) return hints;

        // Look for test files matching the source file name
        var baseName = Path.GetFileNameWithoutExtension(_sourceFile);
        var patterns = new[] { $"{baseName}Tests.cs", $"{baseName}Test.cs", $"Test{baseName}.cs" };

        // Search parent directories for test projects
        var searchDir = sourceDir;
        for (int i = 0; i < 4 && searchDir is not null; i++)
        {
            try
            {
                foreach (var pattern in patterns)
                {
                    var matches = Directory.GetFiles(searchDir, pattern, SearchOption.AllDirectories);
                    hints.AddRange(matches.Take(3));
                }
            }
            catch (Exception ex)
            {
                _log($"  [debug] Directory search failed for {searchDir}: {ex.Message}");
            }

            searchDir = Path.GetDirectoryName(searchDir);
            if (hints.Count > 0) break;
        }

        return hints.Distinct().Take(5).ToList();
    }

    private sealed record SyntaxContext
    {
        public required List<MethodContext> Methods { get; init; }
    }

    private sealed record MethodContext
    {
        public required string ClassName { get; init; }
        public required string MethodName { get; init; }
        public required string Signature { get; init; }
        public required int StartLine { get; init; }
        public required int EndLine { get; init; }
    }

    private static async Task<bool> WaitForFileChange(string dir, CancellationToken ct)
    {
        using var watcher = new FileSystemWatcher(dir)
        {
            Filter = "*.cs",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        var tcs = new TaskCompletionSource<bool>();
        watcher.Changed += (_, _) => tcs.TrySetResult(true);
        watcher.Created += (_, _) => tcs.TrySetResult(true);
        watcher.EnableRaisingEvents = true;

        var completed = await Task.WhenAny(
            tcs.Task,
            Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => false));

        if (ct.IsCancellationRequested) return false;

        // Debounce
        await Task.Delay(500).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Protocol mode: reads commands from stdin, writes JSONL to stdout.
    /// Enables programmatic bidirectional control by an AI orchestrator.
    /// Logging goes to stderr to keep stdout clean for protocol messages.
    /// </summary>
    public async Task<int> RunProtocolAsync(
        TextReader? input = null,
        TextWriter? output = null,
        CancellationToken ct = default)
    {
        input ??= Console.In;
        output ??= Console.Out;
        var errLog = Console.Error;

        await WriteProtocol(output, new ProtocolStatusMessage
        {
            Message = "Agent protocol started",
            Phase = "init"
        });

        List<string>? survivorIds = null;
        var skippedIds = new HashSet<string>();
        string? focusMethod = null;

        for (int round = 1; round <= _maxRounds; round++)
        {
            errLog.WriteLine($"  Round {round}/{_maxRounds}...");

            await WriteProtocol(output, new ProtocolStatusMessage
            {
                Message = $"Starting round {round}",
                Phase = "generate"
            });

            var result = await RunRoundAsync(survivorIds, ct);

            // Apply skips
            if (skippedIds.Count > 0)
            {
                result.Survivors = result.Survivors
                    .Where(s => !skippedIds.Contains(s.Id))
                    .ToList();
            }

            // Apply focus filter
            if (focusMethod is not null)
            {
                result.Survivors = result.Survivors
                    .Where(s => s.ClassName?.Contains(focusMethod) == true
                             || s.MethodSignature?.Contains(focusMethod) == true)
                    .ToList();
            }

            // Write survivors as protocol message
            await WriteProtocol(output, new
            {
                type = "survivors",
                round,
                result = new
                {
                    result.Score,
                    result.TotalMutations,
                    result.Killed,
                    result.AllKilled,
                    result.Message,
                    result.TestFileHints,
                    result.Survivors
                }
            });

            if (result.AllKilled || result.Survivors.Count == 0)
            {
                await WriteProtocol(output, new ProtocolCompleteMessage
                {
                    FinalScore = result.Score,
                    TotalRounds = round,
                    Message = "All mutants killed!"
                });
                return 0;
            }

            survivorIds = result.Survivors.Select(s => s.Id).ToList();

            // Wait for command from stdin
            errLog.WriteLine("  Waiting for agent command...");
            var commandLine = await input.ReadLineAsync(ct);
            if (commandLine is null)
            {
                errLog.WriteLine("  stdin closed, ending protocol.");
                break;
            }

            try
            {
                var command = JsonSerializer.Deserialize<AgentCommand>(commandLine, JsonOptions);
                if (command is null) continue;

                switch (command.Type)
                {
                    case "stop":
                        await WriteProtocol(output, new ProtocolCompleteMessage
                        {
                            FinalScore = result.Score,
                            TotalRounds = round,
                            Message = "Stopped by agent command"
                        });
                        return 0;

                    case "skip" when command.MutationId is not null:
                        skippedIds.Add(command.MutationId);
                        survivorIds.Remove(command.MutationId);
                        errLog.WriteLine($"  Skipping mutation {command.MutationId}");
                        break;

                    case "focus" when command.MethodName is not null:
                        focusMethod = command.MethodName;
                        errLog.WriteLine($"  Focusing on method: {focusMethod}");
                        break;

                    case "continue":
                        break;

                    default:
                        errLog.WriteLine($"  Unknown command: {command.Type}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                errLog.WriteLine($"  Invalid command JSON: {ex.Message}");
            }
        }

        await WriteProtocol(output, new ProtocolCompleteMessage
        {
            FinalScore = 0,
            TotalRounds = _maxRounds,
            Message = $"Max rounds ({_maxRounds}) reached"
        });
        return 1;
    }

    private static async Task WriteProtocol<T>(TextWriter output, T message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await output.WriteLineAsync(json);
        await output.FlushAsync();
    }

    private static string SuggestTestApproach(MutationResultEntry survivor) => survivor.Strategy switch
    {
        "boundary" => $"Test exact boundary value where '{survivor.OriginalCode?.Trim()}' differs from '{survivor.MutatedCode?.Trim()}'",
        "return-value" => "Assert the actual computed return value, not just non-null/non-default",
        "exception" => "Test that invalid input throws the expected exception type",
        "logic-inversion" => "Test both branches of the condition with inputs that exercise each path",
        "semantic" => "Test ordering/selection behavior — verify first vs last, any vs all",
        _ => "Write a test that fails when this specific mutation is applied"
    };
}

// ── Models for agent communication ──────────────────────────────

public sealed class AgentRoundResult
{
    public double Score { get; init; }
    public int TotalMutations { get; init; }
    public int Killed { get; init; }
    public List<SurvivorInfo> Survivors { get; set; } = [];
    public bool AllKilled { get; init; }
    public string Message { get; init; } = "";

    /// <summary>Paths to test files that likely test the mutated source.</summary>
    public List<string> TestFileHints { get; init; } = [];
}

public sealed record SurvivorInfo
{
    public string Id { get; init; } = "";
    public string Strategy { get; init; } = "";
    public string Description { get; init; } = "";
    public string RiskLevel { get; init; } = "";
    public string OriginalCode { get; init; } = "";
    public string MutatedCode { get; init; } = "";
    public int? LineNumber { get; init; }
    public string SuggestedTestApproach { get; init; } = "";

    // ── Enhanced context (populated based on --context-level) ──

    /// <summary>Full method signature containing the mutation.</summary>
    public string? MethodSignature { get; init; }

    /// <summary>Class name containing the mutation.</summary>
    public string? ClassName { get; init; }

    /// <summary>Source code lines around the mutation (5 before/after, with line numbers).</summary>
    public string? SurroundingCode { get; init; }

    /// <summary>Names of test methods that ran against this mutation.</summary>
    public List<string>? RelevantTestNames { get; init; }
}
