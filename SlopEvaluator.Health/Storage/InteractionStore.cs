using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Storage;

/// <summary>
/// JSON file-backed store for prompt interactions.
/// One file per domain, stored in a configurable directory.
/// </summary>
public class InteractionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _dataDirectory;
    private readonly ILogger<InteractionStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InteractionStore"/> class.
    /// </summary>
    /// <param name="dataDirectory">Root directory for storing interaction files.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public InteractionStore(string dataDirectory, ILogger<InteractionStore>? logger = null)
    {
        _dataDirectory = dataDirectory;
        _logger = logger ?? NullLogger<InteractionStore>.Instance;
        Directory.CreateDirectory(_dataDirectory);
    }

    /// <summary>
    /// Save or update a prompt interaction to the store.
    /// </summary>
    public async Task SaveAsync(PromptInteraction interaction)
    {
        _logger.LogInformation("Saving interaction for domain '{Domain}', Id: {Id}", interaction.Domain, interaction.Id);
        var all = await LoadAllAsync(interaction.Domain);
        var existing = all.FindIndex(i => i.Id == interaction.Id);
        if (existing >= 0)
            all[existing] = interaction;
        else
            all.Add(interaction);

        await WriteFileAsync(interaction.Domain, all);
    }

    /// <summary>
    /// Load all interactions, optionally filtered by domain.
    /// </summary>
    public async Task<List<PromptInteraction>> LoadAllAsync(string? domain = null)
    {
        _logger.LogInformation("Loading interactions for domain: {Domain}", domain ?? "(all)");
        if (domain is not null)
            return await ReadFileAsync(domain);

        var all = new List<PromptInteraction>();
        foreach (var file in Directory.GetFiles(_dataDirectory, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file);
            var items = JsonSerializer.Deserialize<List<PromptInteraction>>(json, JsonOptions);
            if (items is not null)
                all.AddRange(items);
        }
        _logger.LogInformation("Loaded {Count} total interactions across all domains", all.Count);
        return all;
    }

    /// <summary>
    /// List all domain names that have stored interactions.
    /// </summary>
    public async Task<List<string>> ListDomainsAsync()
    {
        await Task.CompletedTask;
        return Directory.GetFiles(_dataDirectory, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
    }

    private async Task<List<PromptInteraction>> ReadFileAsync(string domain)
    {
        var path = GetPath(domain);
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<PromptInteraction>>(json, JsonOptions) ?? [];
    }

    private async Task WriteFileAsync(string domain, List<PromptInteraction> interactions)
    {
        var json = JsonSerializer.Serialize(interactions, JsonOptions);
        await File.WriteAllTextAsync(GetPath(domain), json);
    }

    private string GetPath(string domain) =>
        Path.Combine(_dataDirectory, $"{domain}.json");
}
