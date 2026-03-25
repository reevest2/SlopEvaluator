using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Handles config inheritance (extends) and composition (include/merge).
/// </summary>
public static class ConfigMerger
{
    /// <summary>
    /// Resolves a config's includes and extends, returning a fully merged config.
    /// </summary>
    public static HarnessConfig Resolve(HarnessConfig config, string? configDir = null)
    {
        configDir ??= Directory.GetCurrentDirectory();

        // Step 1: Resolve "extends" — inherit base config properties
        if (config.Extends is not null)
        {
            var basePath = ResolvePath(config.Extends, configDir);
            var baseConfig = ReportSerializer.LoadConfigRaw(basePath);
            // Recursively resolve the base config too
            baseConfig = Resolve(baseConfig, Path.GetDirectoryName(basePath));

            config = config with
            {
                SourceFile = string.IsNullOrWhiteSpace(config.SourceFile) ? baseConfig.SourceFile : config.SourceFile,
                ProjectPath = string.IsNullOrWhiteSpace(config.ProjectPath) ? baseConfig.ProjectPath : config.ProjectPath,
                TestCommand = string.IsNullOrWhiteSpace(config.TestCommand) ? baseConfig.TestCommand : config.TestCommand,
                Target = config.Target ?? baseConfig.Target,
                TestTimeoutSeconds = config.TestTimeoutSeconds == 120 ? baseConfig.TestTimeoutSeconds : config.TestTimeoutSeconds,
                // Merge mutations: base first, then this config's additions
                Mutations = [..baseConfig.Mutations, ..config.Mutations],
                Extends = null // Clear to prevent re-resolution
            };
        }

        // Step 2: Resolve "include" — merge mutation lists from other configs
        if (config.Include is { Count: > 0 })
        {
            var additionalMutations = new List<MutationSpec>();
            foreach (var includePath in config.Include)
            {
                var resolvedPath = ResolvePath(includePath, configDir);
                var included = ReportSerializer.LoadConfigRaw(resolvedPath);
                included = Resolve(included, Path.GetDirectoryName(resolvedPath));
                additionalMutations.AddRange(included.Mutations);
            }

            config = config with
            {
                Mutations = [..config.Mutations, ..additionalMutations],
                Include = null // Clear to prevent re-resolution
            };
        }

        // Step 3: Deduplicate mutations by ID (keep first occurrence)
        var seen = new HashSet<string>();
        var deduped = config.Mutations.Where(m => seen.Add(m.Id)).ToList();
        config = config with { Mutations = deduped };

        return config;
    }

    /// <summary>
    /// Merges multiple configs into one. First config provides base properties,
    /// subsequent configs contribute their mutations.
    /// </summary>
    public static HarnessConfig Merge(params HarnessConfig[] configs)
    {
        if (configs.Length == 0)
            throw new ArgumentException("At least one config is required.");

        var baseConfig = configs[0];
        var allMutations = configs.SelectMany(c => c.Mutations).ToList();

        // Deduplicate by ID
        var seen = new HashSet<string>();
        var deduped = allMutations.Where(m => seen.Add(m.Id)).ToList();

        return baseConfig with { Mutations = deduped };
    }

    private static string ResolvePath(string path, string? baseDir)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(baseDir ?? ".", path));
    }
}
