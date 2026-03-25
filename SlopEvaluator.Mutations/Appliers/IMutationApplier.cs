using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Appliers;

/// <summary>
/// Interface for mutation application strategies.
/// Text-based (MutationApplier) and AST-based (RoslynMutationApplier) both implement this.
/// </summary>
public interface IMutationApplier : IDisposable
{
    /// <summary>
    /// Applies a single mutation to the source file.
    /// </summary>
    /// <param name="mutation">The mutation specification describing the code change.</param>
    /// <returns>An <see cref="ApplyResult"/> indicating success or failure with error details.</returns>
    ApplyResult Apply(MutationSpec mutation);

    /// <summary>
    /// Reverts the source file to its original content before the last mutation.
    /// </summary>
    void Revert();

    /// <summary>
    /// Verifies that the source file matches the original content captured at construction time.
    /// </summary>
    /// <returns><c>true</c> if the file matches the original; <c>false</c> if it has been corrupted.</returns>
    bool VerifyOriginal();

    /// <summary>
    /// Restores the source file from the backup copy, overwriting any current content.
    /// Use as a last resort when <see cref="Revert"/> and <see cref="VerifyOriginal"/> fail.
    /// </summary>
    void ForceRestore();
}
