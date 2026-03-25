using SlopEvaluator.Health.Models;
using SlopEvaluator.Mutations.Models;

namespace SlopEvaluator.Orchestrator;

/// <summary>
/// Bridges mutation testing results back into the health score model.
/// Computes updated TestingStrategy metrics from mutation reports.
/// </summary>
public sealed class FeedbackLoop
{
    /// <summary>
    /// Update a Codebase health result with mutation testing outcomes.
    /// </summary>
    /// <param name="original">The original codebase health result.</param>
    /// <param name="reports">Mutation reports from running mutations on target files.</param>
    /// <returns>Updated Codebase with revised testing metrics.</returns>
    public Codebase UpdateHealthScore(Codebase original, List<MutationReport> reports)
    {
        if (reports.Count == 0)
            return original;

        // Aggregate mutation results across all reports
        int totalKilled = reports.Sum(r => r.Killed);
        int totalSurvived = reports.Sum(r => r.Survived);
        int totalValid = totalKilled + totalSurvived;

        double newMutationScore = totalValid == 0
            ? original.Testing.MutationScore
            : (double)totalKilled / totalValid;

        // Blend the new mutation score with the existing one
        // Weight recent results higher but don't discard existing data
        double blendedMutationScore = original.Testing.MutationScore > 0
            ? (original.Testing.MutationScore * 0.4 + newMutationScore * 0.6)
            : newMutationScore;

        // Update MutationTesting profile
        var updatedMutationTesting = original.Testing.MutationTesting with
        {
            MutationScore = blendedMutationScore,
            TotalMutants = original.Testing.MutationTesting.TotalMutants + totalValid,
            Killed = original.Testing.MutationTesting.Killed + totalKilled,
            Survived = original.Testing.MutationTesting.Survived + totalSurvived
        };

        // Update TestingStrategy with blended scores
        var updatedTesting = original.Testing with
        {
            MutationScore = blendedMutationScore,
            MutationTesting = updatedMutationTesting
        };

        return original with { Testing = updatedTesting };
    }
}
