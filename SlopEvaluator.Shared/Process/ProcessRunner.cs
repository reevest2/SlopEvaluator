using System.Diagnostics;

namespace SlopEvaluator.Shared.Process;

/// <summary>Runs external processes (dotnet, git) with timeout and output capture.</summary>
public static class ProcessRunner
{
    public static async Task<(int exitCode, string stdout, string stderr)> RunAsync(
        string command, string arguments, string workingDirectory, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);
        if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
        {
            process.Kill(true);
            return (-1, await stdoutTask, "Process timed out");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
