using System.Runtime.CompilerServices;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

[assembly: InternalsVisibleTo("SlopEvaluator.Mutations.Tests")]

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Shared helpers for line-ending-safe string splitting.
/// Handles CRLF (\r\n), CR (\r), and LF (\n) consistently.
/// </summary>
internal static class LineHelpers
{
    private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

    /// <summary>
    /// Splits text into lines, handling all line ending styles.
    /// </summary>
    internal static string[] SplitLines(string content)
        => content.Split(LineSeparators, StringSplitOptions.None);

    /// <summary>
    /// Detects the dominant line ending style in the content.
    /// Returns "\r\n" for CRLF, "\n" for LF, or Environment.NewLine as fallback.
    /// </summary>
    internal static string DetectLineEnding(string content)
    {
        int crlf = 0, lf = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                crlf++;
                i++; // skip the \n
            }
            else if (content[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf + lf == 0) return Environment.NewLine;
        return crlf >= lf ? "\r\n" : "\n";
    }
}
