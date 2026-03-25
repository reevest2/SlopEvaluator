namespace SlopEvaluator.Health.Models;

/// <summary>
/// Story clarity, acceptance criteria quality, testability, and traceability to code.
/// </summary>
public sealed record RequirementsQuality : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for requirements clarity.</summary>
    public required double Clarity { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for requirements completeness.</summary>
    public required double Completeness { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for testability of requirements.</summary>
    public required double Testability { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for story atomicity.</summary>
    public required double Atomicity { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for acceptance criteria quality.</summary>
    public required double AcceptanceCriteriaQuality { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for traceability from requirements to code.</summary>
    public required double TraceabilityToCode { get; init; }

    /// <summary>Individual story-level assessments.</summary>
    public required List<StoryAssessment> Stories { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (Clarity, 0.20),
        (Completeness, 0.20),
        (Testability, 0.25),
        (Atomicity, 0.10),
        (AcceptanceCriteriaQuality, 0.15),
        (TraceabilityToCode, 0.10)
    );
}

/// <summary>
/// Quality assessment for a single user story or requirement.
/// </summary>
public sealed record StoryAssessment
{
    /// <summary>Identifier for the story (e.g. ticket number).</summary>
    public required string StoryId { get; init; }

    /// <summary>Title or summary of the story.</summary>
    public required string Title { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for clarity of the story.</summary>
    public required double ClarityScore { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for testability of the story.</summary>
    public required double TestabilityScore { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for completeness of the story.</summary>
    public required double CompletenessScore { get; init; }

    /// <summary>Number of acceptance criteria defined.</summary>
    public required int AcceptanceCriteriaCount { get; init; }

    /// <summary>Whether acceptance criteria use Given/When/Then format.</summary>
    public required bool HasGivenWhenThen { get; init; }

    /// <summary>Ambiguous terms found in the story text.</summary>
    public required List<string> AmbiguousTerms { get; init; }

    /// <summary>Scenarios that appear to be missing from acceptance criteria.</summary>
    public required List<string> MissingScenarios { get; init; }

    /// <summary>Estimated number of tests needed to cover the story.</summary>
    public required double EstimatedTestCount { get; init; }
}
