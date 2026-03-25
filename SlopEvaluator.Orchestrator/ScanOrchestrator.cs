using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Collectors;
using SlopEvaluator.Health.Models;
using SlopEvaluator.Health.Storage;

namespace SlopEvaluator.Orchestrator;

/// <summary>
/// Wraps CodebaseCollector + SnapshotStore to produce a full health scan
/// with automatic snapshot persistence and delta computation.
/// </summary>
public sealed class ScanOrchestrator
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly SnapshotStore _snapshotStore;

    public ScanOrchestrator(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

        var dataDir = Environment.GetEnvironmentVariable("SLOP_EVAL_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".slopeval");

        _snapshotStore = new SnapshotStore(dataDir,
            _loggerFactory.CreateLogger<SnapshotStore>());
    }

    /// <summary>
    /// Run a full 14-dimension health scan, save a snapshot, and return the result with delta.
    /// </summary>
    /// <param name="path">Path to the project root.</param>
    /// <param name="name">Optional codebase name; auto-detected from directory name if null.</param>
    /// <returns>The Codebase result and optional delta from previous snapshot.</returns>
    public async Task<(Codebase Result, SnapshotDelta? Delta)> ScanAsync(string path, string? name = null)
    {
        path = Path.GetFullPath(path);
        name ??= Path.GetFileName(path);

        var logger = _loggerFactory.CreateLogger<CodebaseCollector>();

        // Wire up InteractionStore for AI quality metrics
        var dataDir = Environment.GetEnvironmentVariable("SLOP_EVAL_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".slopeval");
        var interactionsDir = Path.Combine(dataDir, "interactions");
        var interactionStore = new InteractionStore(interactionsDir,
            _loggerFactory.CreateLogger<InteractionStore>());

        var collector = new CodebaseCollector(interactionStore, logger);
        var codebase = await collector.CollectAsync(path, name, domain: name);

        // Save snapshot and compute delta
        var snapshot = await _snapshotStore.SaveSnapshotAsync(codebase, path);

        return (codebase, snapshot.DeltaFromPrevious);
    }

    /// <summary>
    /// Load all historical snapshots for a codebase.
    /// </summary>
    public Task<List<CodebaseSnapshot>> GetHistoryAsync(string name)
        => _snapshotStore.LoadSnapshotsAsync(name);

    /// <summary>
    /// List all codebases with saved snapshots.
    /// </summary>
    public Task<List<string>> ListCodebasesAsync()
        => _snapshotStore.ListCodebasesAsync();
}
