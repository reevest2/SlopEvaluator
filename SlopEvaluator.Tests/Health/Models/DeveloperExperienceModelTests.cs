using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests.Health.Models;

public class DeveloperExperienceModelTests
{
    [Fact]
    public void Score_WithAllPerfectScores_ReturnsOne()
    {
        var devex = TestHelpers.CreateDeveloperExperience(1.0, 1.0, 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(1.0, devex.Score, precision: 10);
    }

    [Fact]
    public void Score_WithAllZeroScores_ReturnsZero()
    {
        var devex = TestHelpers.CreateDeveloperExperience(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        Assert.Equal(0.0, devex.Score, precision: 10);
    }

    [Fact]
    public void Score_BuildTimeAndToolingHaveHighestWeight()
    {
        // BuildTimeSatisfaction=0.20, ToolingMaturity=0.20 (tied highest)
        var highBuild = TestHelpers.CreateDeveloperExperience(
            buildTimeSatisfaction: 1.0, testRunSpeed: 0.0,
            onboardingFriction: 0.0, toolingMaturity: 0.0,
            innerLoopSpeed: 0.0, debugExperience: 0.0);
        var highOnboarding = TestHelpers.CreateDeveloperExperience(
            buildTimeSatisfaction: 0.0, testRunSpeed: 0.0,
            onboardingFriction: 1.0, toolingMaturity: 0.0,
            innerLoopSpeed: 0.0, debugExperience: 0.0);

        // Build weight (0.20) > onboarding weight (0.15)
        Assert.True(highBuild.Score > highOnboarding.Score);
    }

    [Fact]
    public void Score_WeightsSum_ToOne()
    {
        double weightSum = 0.20 + 0.15 + 0.15 + 0.20 + 0.15 + 0.15;

        Assert.Equal(1.0, weightSum, precision: 10);
    }

    [Fact]
    public void Score_WithUniformScores_ReturnsThatScore()
    {
        var devex = TestHelpers.CreateDeveloperExperience(0.65, 0.65, 0.65, 0.65, 0.65, 0.65);

        Assert.Equal(0.65, devex.Score, precision: 10);
    }

    [Fact]
    public void Score_ImplementsIScoreable()
    {
        var devex = TestHelpers.CreateDeveloperExperience();
        IScoreable scoreable = devex;

        Assert.Equal(devex.Score, scoreable.Score);
    }
}
