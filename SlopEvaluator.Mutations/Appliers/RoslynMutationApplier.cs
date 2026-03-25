using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Strategies;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Appliers;

/// <summary>
/// Applies mutations using Roslyn syntax tree manipulation.
/// Finds exact SyntaxNode by text + line span, eliminating line-number drift.
/// Falls back to text-based replacement for mutations that don't map to AST nodes.
/// </summary>
public sealed class RoslynMutationApplier : IMutationApplier
{
    private readonly string _sourceFile;
    private readonly string _backupFile;
    private readonly string _originalContent;
    private readonly Encoding _encoding;
    private readonly SyntaxTree _originalTree;
    private readonly FileStream? _lockStream;
    private readonly IReadOnlyList<IMutationStrategy> _strategies;
    private bool _disposed;

    public RoslynMutationApplier(string sourceFile)
    {
        _sourceFile = Path.GetFullPath(sourceFile);
        _backupFile = _sourceFile + ".mutation-backup";

        if (!File.Exists(_sourceFile))
            throw new FileNotFoundException($"Source file not found: {_sourceFile}");

        var attrs = File.GetAttributes(_sourceFile);
        if (attrs.HasFlag(FileAttributes.ReadOnly))
            throw new UnauthorizedAccessException(
                $"Source file is read-only: {_sourceFile}. Remove read-only flag before mutation testing.");

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

        // Wrap in try-catch so lock is released if init fails after acquisition
        try
        {
            _encoding = DetectEncoding(_sourceFile);
            _originalContent = File.ReadAllText(_sourceFile, _encoding);
            _originalTree = CSharpSyntaxTree.ParseText(_originalContent, path: _sourceFile);
            File.Copy(_sourceFile, _backupFile, overwrite: true);
        }
        catch
        {
            _lockStream?.Dispose();
            throw;
        }

        _strategies = new IMutationStrategy[]
        {
            new RemoveStatementStrategy(),
            new SwapStatementsStrategy(),
            new EmptyMethodBodyStrategy(),
            new RemoveGuardClauseStrategy()
        };
    }

    public ApplyResult Apply(MutationSpec mutation)
    {
        // Try structural strategy first (for remove-statement, swap-statements, etc.)
        if (mutation.TargetMethod is not null)
        {
            var structuralResult = TryApplyStructural(mutation);
            if (structuralResult is not null)
                return structuralResult;
        }

        // Try AST-based text replacement
        var result = TryApplyViaAst(mutation);
        if (result is not null)
            return result;

        // Fall back to text-based replacement (for Claude-authored multi-line diffs)
        return ApplyViaText(mutation);
    }

    private ApplyResult? TryApplyStructural(MutationSpec mutation)
    {
        var root = _originalTree.GetRoot();

        foreach (var strategy in _strategies)
        {
            if (strategy.Name != mutation.Strategy) continue;

            var newSource = strategy.ApplyStructural(_originalTree, root, mutation);
            if (newSource is not null)
            {
                File.WriteAllText(_sourceFile, newSource, _encoding);
                return new ApplyResult(true, null);
            }
        }

        return null;
    }

    private ApplyResult? TryApplyViaAst(MutationSpec mutation)
    {
        var root = _originalTree.GetRoot();
        var sourceText = _originalTree.GetText();

        // Find all nodes whose text contains the original code
        var candidates = root.DescendantNodes()
            .Where(n => n.ToFullString().Contains(mutation.OriginalCode.Trim()))
            .ToList();

        if (candidates.Count == 0)
            return null; // Fall back to text-based

        SyntaxNode? bestNode = null;

        if (mutation.LineNumberHint.HasValue)
        {
            // Find the node closest to the hinted line
            var targetLine = mutation.LineNumberHint.Value - 1; // 0-based
            bestNode = candidates
                .OrderBy(n => Math.Abs(n.GetLocation().GetLineSpan().StartLinePosition.Line - targetLine))
                .First();
        }
        else if (candidates.Count == 1)
        {
            bestNode = candidates[0];
        }
        else
        {
            return null; // Ambiguous without hint — fall back to text
        }

        // Apply the mutation by replacing the text within this node's span
        var nodeText = bestNode.ToFullString();
        var mutatedNodeText = nodeText.Replace(mutation.OriginalCode.Trim(), mutation.MutatedCode);

        if (mutatedNodeText == nodeText)
            return null; // Replacement didn't change anything — fall back

        var span = bestNode.FullSpan;
        var newSource = string.Concat(
            _originalContent.AsSpan(0, span.Start),
            mutatedNodeText,
            _originalContent.AsSpan(span.End));

        File.WriteAllText(_sourceFile, newSource, _encoding);
        return new ApplyResult(true, null);
    }

    private ApplyResult ApplyViaText(MutationSpec mutation)
    {
        var content = _originalContent;

        // Line hint validation
        if (mutation.LineNumberHint.HasValue)
        {
            var lines = LineHelpers.SplitLines(content);
            var lineIdx = mutation.LineNumberHint.Value - 1;

            if (lineIdx < 0 || lineIdx >= lines.Length)
                return new ApplyResult(false, "Line number hint out of range");

            var windowStart = Math.Max(0, lineIdx - 5);
            var windowEnd = Math.Min(lines.Length - 1, lineIdx + 5);
            var window = string.Join('\n', lines[windowStart..(windowEnd + 1)]);

            if (!window.Contains(mutation.OriginalCode.Trim()))
                return new ApplyResult(false,
                    $"Original code not found near line {mutation.LineNumberHint}");
        }

        var idx = content.IndexOf(mutation.OriginalCode, StringComparison.Ordinal);
        if (idx < 0)
            return new ApplyResult(false, "Original code not found in source file");

        var mutated = string.Concat(
            content.AsSpan(0, idx),
            mutation.MutatedCode,
            content.AsSpan(idx + mutation.OriginalCode.Length));

        File.WriteAllText(_sourceFile, mutated, _encoding);
        return new ApplyResult(true, null);
    }

    public void Revert()
    {
        File.WriteAllText(_sourceFile, _originalContent, _encoding);
    }

    public bool VerifyOriginal()
    {
        var current = File.ReadAllText(_sourceFile, _encoding);
        return current == _originalContent;
    }

    public void ForceRestore()
    {
        if (File.Exists(_backupFile))
            File.Copy(_backupFile, _sourceFile, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ForceRestore();

        try { if (File.Exists(_backupFile)) File.Delete(_backupFile); }
        catch { }

        try { _lockStream?.Dispose(); }
        catch { }
    }

    private static Encoding DetectEncoding(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        reader.Peek();
        return reader.CurrentEncoding;
    }
}
