using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Storage;

/// <summary>
/// JSON file-backed store for codebase snapshots.
/// One file per codebase, stored in ~/.prompteval/snapshots/.
/// </summary>
public class SnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _snapshotDir;
    private readonly ILogger<SnapshotStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotStore"/> class.
    /// </summary>
    /// <param name="dataDirectory">Root directory for storing snapshot files.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public SnapshotStore(string dataDirectory, ILogger<SnapshotStore>? logger = null)
    {
        _snapshotDir = Path.Combine(dataDirectory, "snapshots");
        _logger = logger ?? NullLogger<SnapshotStore>.Instance;
        Directory.CreateDirectory(_snapshotDir);
    }

    /// <summary>
    /// Save a snapshot, computing delta from the previous one automatically.
    /// </summary>
    public async Task<CodebaseSnapshot> SaveSnapshotAsync(Codebase codebase, string? repoPath = null)
    {
        _logger.LogInformation("Saving snapshot for {Name} with score {Score:F3}", codebase.Name, codebase.Score);
        var previous = await GetLatestAsync(codebase.Name);

        string gitSha = "";
        string gitBranch = "";
        if (repoPath is not null)
        {
            gitSha = await GetGitShaAsync(repoPath);
            gitBranch = await GetGitBranchAsync(repoPath);
        }

        var snapshot = new CodebaseSnapshot
        {
            Id = Guid.NewGuid(),
            CodebaseName = codebase.Name,
            TakenAt = DateTime.UtcNow,
            GitCommitSha = gitSha,
            GitBranch = gitBranch,
            Data = codebase,
            DeltaFromPrevious = previous is not null
                ? ComputeDelta(codebase, previous)
                : null
        };

        var all = await LoadSnapshotsAsync(codebase.Name);
        all.Add(snapshot);
        await WriteFileAsync(codebase.Name, all);

        return snapshot;
    }

    /// <summary>
    /// Load all saved snapshots for a codebase, ordered by time.
    /// </summary>
    public async Task<List<CodebaseSnapshot>> LoadSnapshotsAsync(string codebaseName)
    {
        var path = GetPath(codebaseName);
        if (!File.Exists(path)) return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<CodebaseSnapshot>>(json, JsonOptions) ?? [];
    }

    /// <summary>
    /// Get the most recent snapshot for a codebase.
    /// </summary>
    public async Task<CodebaseSnapshot?> GetLatestAsync(string codebaseName)
    {
        var snapshots = await LoadSnapshotsAsync(codebaseName);
        return snapshots.OrderByDescending(s => s.TakenAt).FirstOrDefault();
    }

    /// <summary>
    /// List all codebase names that have saved snapshots.
    /// </summary>
    public async Task<List<string>> ListCodebasesAsync()
    {
        await Task.CompletedTask;
        if (!Directory.Exists(_snapshotDir)) return [];
        return Directory.GetFiles(_snapshotDir, "*.snapshots.json")
            .Select(f => Path.GetFileNameWithoutExtension(f).Replace(".snapshots", ""))
            .ToList();
    }

    /// <summary>
    /// Compute the delta between current data and a previous snapshot.
    /// </summary>
    public static SnapshotDelta ComputeDelta(Codebase current, CodebaseSnapshot previous)
    {
        var prev = previous.Data;
        var deltas = new Dictionary<string, double>
        {
            ["CodeQuality"] = current.Quality.Score - prev.Quality.Score,
            ["Testing"] = current.Testing.Score - prev.Testing.Score,
            ["Dependencies"] = current.Dependencies.Score - prev.Dependencies.Score,
            ["Security"] = current.Security.Score - prev.Security.Score,
            ["Observability"] = current.Observability.Score - prev.Observability.Score,
            ["CiCd"] = current.Pipeline.Score - prev.Pipeline.Score,
            ["Documentation"] = current.Documentation.Score - prev.Documentation.Score,
            ["DevExperience"] = current.DevEx.Score - prev.DevEx.Score,
            ["Performance"] = current.Performance.Score - prev.Performance.Score,
            ["Requirements"] = current.Requirements.Score - prev.Requirements.Score,
            ["TeamProcess"] = current.Process.Score - prev.Process.Score,
            ["AIQuality"] = current.AIQuality.Score - prev.AIQuality.Score,
            ["Architecture"] = current.Architecture.Score - prev.Architecture.Score,
            ["Structure"] = current.Structure.Score - prev.Structure.Score
        };

        var improvements = deltas
            .Where(kv => kv.Value > 0.01)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key}: +{kv.Value:F3}")
            .ToList();

        var regressions = deltas
            .Where(kv => kv.Value < -0.01)
            .OrderBy(kv => kv.Value)
            .Select(kv => $"{kv.Key}: {kv.Value:F3}")
            .ToList();

        return new SnapshotDelta
        {
            PreviousSnapshotId = previous.Id,
            TimeBetween = DateTime.UtcNow - previous.TakenAt,
            OverallScoreDelta = current.Score - prev.Score,
            DimensionDeltas = deltas,
            Improvements = improvements,
            Regressions = regressions
        };
    }

    private async Task WriteFileAsync(string codebaseName, List<CodebaseSnapshot> snapshots)
    {
        var json = JsonSerializer.Serialize(snapshots, JsonOptions);
        await File.WriteAllTextAsync(GetPath(codebaseName), json);
    }

    private string GetPath(string codebaseName) =>
        Path.Combine(_snapshotDir, $"{codebaseName}.snapshots.json");

    private static async Task<string> GetGitShaAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git", Arguments = "rev-parse HEAD",
                WorkingDirectory = path,
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }

    private static async Task<string> GetGitBranchAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git", Arguments = "rev-parse --abbrev-ref HEAD",
                WorkingDirectory = path,
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }
}
