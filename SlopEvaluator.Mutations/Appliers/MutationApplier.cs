using System.Text;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Appliers;

/// <summary>
/// Handles safe backup/restore and applying mutations to source files.
/// Thread-safe via file locking. Preserves file encoding through round-trips.
/// </summary>
public sealed class MutationApplier : IMutationApplier
{
    private readonly string _sourceFile;
    private readonly string _backupFile;
    private readonly string _originalContent;
    private readonly Encoding _encoding;
    private readonly FileStream? _lockStream;
    private bool _disposed;

    public MutationApplier(string sourceFile)
    {
        _sourceFile = Path.GetFullPath(sourceFile);
        _backupFile = _sourceFile + ".mutation-backup";

        if (!File.Exists(_sourceFile))
            throw new FileNotFoundException($"Source file not found: {_sourceFile}");

        // Check write access before doing anything
        var attrs = File.GetAttributes(_sourceFile);
        if (attrs.HasFlag(FileAttributes.ReadOnly))
            throw new UnauthorizedAccessException(
                $"Source file is read-only: {_sourceFile}. Remove read-only flag before mutation testing.");

        // Acquire exclusive lock to prevent concurrent mutation runs on same file
        try
        {
            _lockStream = new FileStream(
                _sourceFile + ".lock",
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            throw new InvalidOperationException(
                $"Another mutation harness instance is running on {_sourceFile}. " +
                "Wait for it to finish or delete the .lock file.");
        }

        // Detect and preserve file encoding (BOM-aware)
        // Wrap in try-catch so lock is released if backup/read fails
        try
        {
            _encoding = DetectEncoding(_sourceFile);
            _originalContent = File.ReadAllText(_sourceFile, _encoding);
            File.Copy(_sourceFile, _backupFile, overwrite: true);
        }
        catch
        {
            _lockStream?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Detects file encoding by checking BOM. Falls back to UTF-8 no BOM.
    /// </summary>
    private static Encoding DetectEncoding(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        reader.Peek(); // Force encoding detection
        return reader.CurrentEncoding;
    }

    /// <summary>
    /// Applies a single mutation to the source file.
    /// Returns true if the replacement was made, false if original code wasn't found.
    /// </summary>
    public ApplyResult Apply(MutationSpec mutation)
    {
        var content = _originalContent;

        // If line number hint is provided, validate the original code is near that line
        if (mutation.LineNumberHint.HasValue)
        {
            var lines = LineHelpers.SplitLines(content);
            var lineIdx = mutation.LineNumberHint.Value - 1; // 0-based

            if (lineIdx < 0 || lineIdx >= lines.Length)
                return new ApplyResult(false, "Line number hint out of range");

            // Check a window of ±5 lines around the hint
            var windowStart = Math.Max(0, lineIdx - 5);
            var windowEnd = Math.Min(lines.Length - 1, lineIdx + 5);
            var window = string.Join('\n', lines[windowStart..(windowEnd + 1)]);

            if (!window.Contains(mutation.OriginalCode.Trim()))
                return new ApplyResult(false,
                    $"Original code not found near line {mutation.LineNumberHint}");
        }

        // Count occurrences to avoid ambiguous replacements
        var occurrences = CountOccurrences(content, mutation.OriginalCode);

        if (occurrences == 0)
            return new ApplyResult(false, "Original code not found in source file");

        if (occurrences > 1 && !mutation.LineNumberHint.HasValue)
            return new ApplyResult(false,
                $"Ambiguous: original code found {occurrences} times. " +
                "Provide a LineNumberHint to disambiguate.");

        // Apply the mutation
        string mutated;
        if (occurrences > 1 && mutation.LineNumberHint.HasValue)
        {
            mutated = ReplaceNearLine(content, mutation.OriginalCode,
                mutation.MutatedCode, mutation.LineNumberHint.Value);
        }
        else
        {
            // Single occurrence — safe to replace first match
            var idx = content.IndexOf(mutation.OriginalCode, StringComparison.Ordinal);
            mutated = string.Concat(
                content.AsSpan(0, idx),
                mutation.MutatedCode,
                content.AsSpan(idx + mutation.OriginalCode.Length));
        }

        File.WriteAllText(_sourceFile, mutated, _encoding);
        return new ApplyResult(true, null);
    }

    /// <summary>
    /// Reverts the source file to its original content.
    /// </summary>
    public void Revert()
    {
        File.WriteAllText(_sourceFile, _originalContent, _encoding);
    }

    /// <summary>
    /// Verifies the source file matches the original content exactly.
    /// </summary>
    public bool VerifyOriginal()
    {
        var current = File.ReadAllText(_sourceFile, _encoding);
        return current == _originalContent;
    }

    /// <summary>
    /// Force-restores from the backup file. Use if something goes wrong.
    /// </summary>
    public void ForceRestore()
    {
        if (File.Exists(_backupFile))
            File.Copy(_backupFile, _sourceFile, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Always restore original on disposal
        ForceRestore();

        // Clean up backup
        try { if (File.Exists(_backupFile)) File.Delete(_backupFile); }
        catch { /* best effort cleanup */ }

        // Release file lock (last — other instances can now proceed)
        try { _lockStream?.Dispose(); }
        catch { /* best effort */ }
    }

    private static int CountOccurrences(string text, string search)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }

    private static string ReplaceNearLine(string content, string original,
        string replacement, int lineHint)
    {
        var lineEnding = LineHelpers.DetectLineEnding(content);
        var lines = LineHelpers.SplitLines(content);
        // Find the character offset of the target line
        int charOffset = 0;
        for (int i = 0; i < Math.Min(lineHint - 1, lines.Length); i++)
            charOffset += lines[i].Length + lineEnding.Length;

        // Search for the occurrence nearest to this offset
        int bestIdx = -1;
        int bestDist = int.MaxValue;
        int searchIdx = 0;

        while ((searchIdx = content.IndexOf(original, searchIdx, StringComparison.Ordinal)) >= 0)
        {
            var dist = Math.Abs(searchIdx - charOffset);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = searchIdx;
            }
            searchIdx += original.Length;
        }

        if (bestIdx < 0) return content;

        return string.Concat(
            content.AsSpan(0, bestIdx),
            replacement,
            content.AsSpan(bestIdx + original.Length));
    }
}

public sealed record ApplyResult(bool Success, string? Error);
