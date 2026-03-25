using SlopEvaluator.Health.Collectors;

namespace SlopEvaluator.Tests.Health.Collectors;

public class TestingStrategyCollectorTests
{
    [Fact]
    public void AggregateMutationReports_EmptyList_ReturnsZeroProfile()
    {
        var result = TestingStrategyCollector.AggregateMutationReports([]);

        Assert.Equal(0, result.TotalMutants);
        Assert.Equal(0, result.Killed);
        Assert.Equal(0, result.Survived);
        Assert.Equal(0, result.CompileErrors);
        Assert.Equal(0.0, result.MutationScore);
        Assert.Equal(0.0, result.MutationsPerMinute);
        Assert.Empty(result.ScoreByStrategy);
        Assert.Empty(result.SurvivorPatterns);
    }

    [Fact]
    public void AggregateMutationReports_AllKilled_ReturnsPerfectScore()
    {
        var reports = new List<TestingStrategyCollector.MutationReportData>
        {
            CreateReport([
                CreateResult("strategy-a", "killed"),
                CreateResult("strategy-a", "killed"),
                CreateResult("strategy-b", "killed"),
            ])
        };

        var result = TestingStrategyCollector.AggregateMutationReports(reports);

        Assert.Equal(1.0, result.MutationScore);
        Assert.Equal(3, result.Killed);
        Assert.Equal(0, result.Survived);
    }

    [Fact]
    public void AggregateMutationReports_MixedResults_CalculatesCorrectScore()
    {
        var reports = new List<TestingStrategyCollector.MutationReportData>
        {
            CreateReport([
                CreateResult("strategy-a", "killed"),
                CreateResult("strategy-a", "survived", "desc1"),
                CreateResult("strategy-b", "killed"),
                CreateResult("strategy-b", "survived", "desc2"),
            ])
        };

        var result = TestingStrategyCollector.AggregateMutationReports(reports);

        // 2 killed, 2 survived => score = 2/4 = 0.5
        Assert.Equal(0.5, result.MutationScore, precision: 10);
        Assert.Equal(2, result.Killed);
        Assert.Equal(2, result.Survived);
    }

    [Fact]
    public void AggregateMutationReports_CompileErrorsExcludedFromScore()
    {
        var reports = new List<TestingStrategyCollector.MutationReportData>
        {
            CreateReport([
                CreateResult("strategy-a", "killed"),
                CreateResult("strategy-a", "compileerror"),
                CreateResult("strategy-a", "compileerror"),
            ])
        };

        var result = TestingStrategyCollector.AggregateMutationReports(reports);

        // Only 1 killed, 0 survived, 2 compile errors
        // Score = 1/1 = 1.0 (compile errors excluded)
        Assert.Equal(1.0, result.MutationScore);
        Assert.Equal(1, result.Killed);
        Assert.Equal(0, result.Survived);
        Assert.Equal(2, result.CompileErrors);
        Assert.Equal(3, result.TotalMutants); // killed + survived + compileErrors
    }

    [Fact]
    public void AggregateMutationReports_TracksScoreByStrategy()
    {
        var reports = new List<TestingStrategyCollector.MutationReportData>
        {
            CreateReport([
                CreateResult("remove-statement", "killed"),
                CreateResult("remove-statement", "survived", "desc"),
                CreateResult("empty-method-body", "killed"),
                CreateResult("empty-method-body", "killed"),
            ])
        };

        var result = TestingStrategyCollector.AggregateMutationReports(reports);

        Assert.Equal(0.5, result.ScoreByStrategy["remove-statement"], precision: 10);
        Assert.Equal(1.0, result.ScoreByStrategy["empty-method-body"], precision: 10);
    }

    [Fact]
    public void AggregateMutationReports_CollectsSurvivorPatterns()
    {
        var reports = new List<TestingStrategyCollector.MutationReportData>
        {
            CreateReport([
                CreateResult("s", "survived", "missing null check"),
                CreateResult("s", "survived", "missing null check"),
                CreateResult("s", "survived", "boundary condition"),
            ])
        };

        var result = TestingStrategyCollector.AggregateMutationReports(reports);

        // "missing null check" appears twice, should be first
        Assert.Equal("missing null check", result.SurvivorPatterns[0]);
        Assert.Contains("boundary condition", result.SurvivorPatterns);
    }

    [Fact]
    public void AggregateMutationReports_MultipleReports_AggregatesAcrossAll()
    {
        var reports = new List<TestingStrategyCollector.MutationReportData>
        {
            CreateReport([CreateResult("s", "killed")], durationMinutes: 2),
            CreateReport([CreateResult("s", "survived", "desc")], durationMinutes: 3),
        };

        var result = TestingStrategyCollector.AggregateMutationReports(reports);

        Assert.Equal(2, result.TotalMutants);
        Assert.Equal(1, result.Killed);
        Assert.Equal(1, result.Survived);
        // 2 mutants / 5 minutes = 0.4 per minute
        Assert.Equal(0.4, result.MutationsPerMinute, precision: 10);
    }

    private static TestingStrategyCollector.MutationReportData CreateReport(
        List<TestingStrategyCollector.MutationResultData> results,
        double durationMinutes = 1.0)
    {
        return new TestingStrategyCollector.MutationReportData
        {
            SourceFile = "test.cs",
            Target = "TestClass",
            RunDate = DateTime.UtcNow,
            TotalDuration = TimeSpan.FromMinutes(durationMinutes),
            Results = results
        };
    }

    private static TestingStrategyCollector.MutationResultData CreateResult(
        string strategy, string outcome, string? description = null)
    {
        return new TestingStrategyCollector.MutationResultData
        {
            Id = Guid.NewGuid().ToString(),
            Strategy = strategy,
            Outcome = outcome,
            Description = description ?? outcome
        };
    }
}
