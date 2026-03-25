using Microsoft.Extensions.Logging;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Simple pre-flight health checks for the mutation harness.
/// Verifies that required resources are available before starting a run.
/// </summary>
public static class HealthCheck
{
    public sealed record HealthResult(bool IsHealthy, List<string> Errors, List<string> Warnings);

    /// <summary>
    /// Run all health checks and return a consolidated result.
    /// </summary>
    public static HealthResult Check(
        string? sourceFile,
        string? testCommand,
        string? outputDirectory = null,
        ILogger? logger = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        CheckSourceFile(sourceFile, errors, logger);
        CheckTestCommand(testCommand, errors, warnings, logger);
        CheckOutputDirectory(outputDirectory, errors, warnings, logger);

        var isHealthy = errors.Count == 0;

        if (isHealthy)
            logger?.LogInformation("Health check passed. All pre-flight checks OK.");
        else
            logger?.LogError("Health check failed with {ErrorCount} error(s).", errors.Count);

        return new HealthResult(isHealthy, errors, warnings);
    }

    private static void CheckSourceFile(string? sourceFile, List<string> errors, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            errors.Add("Source file path is null or empty.");
            logger?.LogError("Source file path is null or empty.");
            return;
        }

        if (!File.Exists(sourceFile))
        {
            errors.Add($"Source file not found: {sourceFile}");
            logger?.LogError("Source file not found: {SourceFile}", sourceFile);
            return;
        }

        logger?.LogDebug("Source file exists: {SourceFile}", sourceFile);
    }

    private static void CheckTestCommand(string? testCommand, List<string> errors,
        List<string> warnings, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(testCommand))
        {
            errors.Add("Test command is null or empty.");
            logger?.LogError("Test command is null or empty.");
            return;
        }

        // Parse the command to extract the executable
        var parts = testCommand.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var executable = parts[0];

        // Basic validation: known .NET test runners
        var knownRunners = new[] { "dotnet", "nunit3-console", "vstest.console" };
        if (!knownRunners.Any(r => executable.Contains(r, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"Test command executable '{executable}' is not a recognized .NET test runner. This may still work.");
            logger?.LogWarning("Test command executable '{Executable}' is not a recognized .NET test runner.", executable);
        }
        else
        {
            logger?.LogDebug("Test command is valid: {TestCommand}", testCommand);
        }
    }

    private static void CheckOutputDirectory(string? outputDirectory, List<string> errors,
        List<string> warnings, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            // Not specified is OK — will use current directory
            logger?.LogDebug("No output directory specified; will use current directory.");
            return;
        }

        if (!Directory.Exists(outputDirectory))
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                logger?.LogInformation("Created output directory: {OutputDirectory}", outputDirectory);
            }
            catch (Exception ex)
            {
                errors.Add($"Cannot create output directory '{outputDirectory}': {ex.Message}");
                logger?.LogError(ex, "Cannot create output directory: {OutputDirectory}", outputDirectory);
                return;
            }
        }

        // Test writability
        var testFile = Path.Combine(outputDirectory, $".healthcheck-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(testFile, "healthcheck");
            File.Delete(testFile);
            logger?.LogDebug("Output directory is writable: {OutputDirectory}", outputDirectory);
        }
        catch (Exception ex)
        {
            errors.Add($"Output directory is not writable: {outputDirectory}");
            logger?.LogError(ex, "Output directory is not writable: {OutputDirectory}", outputDirectory);
        }
    }
}
