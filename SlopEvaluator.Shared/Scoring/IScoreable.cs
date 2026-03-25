namespace SlopEvaluator.Shared.Scoring;

/// <summary>Contract for any measurable dimension with a 0.0–1.0 score.</summary>
public interface IScoreable
{
    double Score { get; }
}
