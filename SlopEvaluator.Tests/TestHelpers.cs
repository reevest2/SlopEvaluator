using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Tests;

/// <summary>
/// Factory methods for building model instances with sensible defaults for testing.
/// </summary>
internal static class TestHelpers
{
    internal static TestingStrategy CreateTestingStrategy(
        double lineCoverage = 0.5, double branchCoverage = 0.5,
        double mutationScore = 0.5, double edgeCaseCoverage = 0.5,
        double testQualityScore = 0.5)
    {
        return new TestingStrategy
        {
            LineCoverage = lineCoverage,
            BranchCoverage = branchCoverage,
            MutationScore = mutationScore,
            EdgeCaseCoverage = edgeCaseCoverage,
            TestQualityScore = testQualityScore,
            Suite = new TestSuiteProfile
            {
                TotalTests = 10, UnitTests = 8, IntegrationTests = 2, EndToEndTests = 0,
                TestToCodeRatio = 0.5, TotalRunTime = TimeSpan.FromSeconds(5),
                AverageTestDuration = TimeSpan.FromMilliseconds(500), FlakyTests = 0,
                Determinism = 1.0, Framework = "xUnit", TestCategories = ["Unit"]
            },
            Quality = new TestQualityProfile
            {
                Trustworthiness = 0.8, Precision = 0.7, Maintainability = 0.9,
                DiagnosticValue = 0.6, AttributeScores = new(), RSquared = 0.0,
                CalibratedWeights = [], TopIssues = []
            },
            MutationTesting = new MutationTestingProfile
            {
                MutationScore = mutationScore, TotalMutants = 10, Killed = 5, Survived = 5,
                CompileErrors = 0, ScoreByStrategy = new(), SurvivorPatterns = [],
                MutationsPerMinute = 10
            },
            Efficiency = new TokenEfficiencyProfile
            {
                TotalTokensSpent = 1000, TotalCostEstimate = 0.01, OverallEfficiency = 0.8,
                OptimalStopRound = 3, DiminishingReturnsLambda = 0.5, Recommendations = []
            }
        };
    }

    internal static Documentation CreateDocumentation(
        double readmeCompleteness = 0.5, double apiDocCoverage = 0.5,
        double adrPresence = 0.5, double docFreshness = 0.5,
        double inlineCommentQuality = 0.5, double onboardingDocumentation = 0.5)
    {
        return new Documentation
        {
            ReadmeCompleteness = readmeCompleteness,
            ApiDocCoverage = apiDocCoverage,
            AdrPresence = adrPresence,
            DocFreshness = docFreshness,
            InlineCommentQuality = inlineCommentQuality,
            OnboardingDocumentation = onboardingDocumentation,
            Inventory = new DocInventory
            {
                ReadmeExists = true, ChangelogExists = false,
                ContributingGuideExists = false, ClaudeMdExists = true,
                AdrCount = 0, PublicMembersTotal = 100, PublicMembersDocumented = 50,
                DocCoverageByProject = new()
            }
        };
    }

    internal static CiCdPipeline CreateCiCdPipeline(
        double buildReliability = 0.5, double buildSpeed = 0.5,
        double deployFrequency = 0.5, double pipelineCompleteness = 0.5,
        double environmentParity = 0.5, double rollbackCapability = 0.5)
    {
        return new CiCdPipeline
        {
            BuildReliability = buildReliability,
            BuildSpeed = buildSpeed,
            DeployFrequency = deployFrequency,
            PipelineCompleteness = pipelineCompleteness,
            EnvironmentParity = environmentParity,
            RollbackCapability = rollbackCapability,
            Build = new BuildProfile
            {
                AverageBuildTime = TimeSpan.FromMinutes(5),
                MedianBuildTime = TimeSpan.FromMinutes(4),
                SuccessRate = 0.95, BuildsPerWeek = 50, WarningTrend = 0,
                IncrementalBuildEnabled = true, CacheEnabled = true
            },
            Deployment = new DeploymentProfile
            {
                DeploysPerWeek = 3, LeadTimeForChanges = TimeSpan.FromHours(4),
                MeanTimeToRecover = TimeSpan.FromHours(1), ChangeFailureRate = 0.05,
                Environments = 3, EnvironmentNames = ["Dev", "Staging", "Prod"],
                BlueGreenOrCanary = false, AutomatedRollback = false
            },
            Stages = []
        };
    }

    internal static TeamProcessMetrics CreateTeamProcess(
        double prCycleTimeHealth = 0.5, double reviewQuality = 0.5,
        double knowledgeDistribution = 0.5, double commitHygiene = 0.5,
        double branchStrategy = 0.5, double incidentResponseHealth = 0.5)
    {
        return new TeamProcessMetrics
        {
            PrCycleTimeHealth = prCycleTimeHealth,
            ReviewQuality = reviewQuality,
            KnowledgeDistribution = knowledgeDistribution,
            CommitHygiene = commitHygiene,
            BranchStrategy = branchStrategy,
            IncidentResponseHealth = incidentResponseHealth,
            PullRequests = new PullRequestMetrics
            {
                MedianTimeToFirstReview = TimeSpan.FromHours(2),
                MedianTimeToMerge = TimeSpan.FromHours(8),
                AverageReviewComments = 3.0, ApprovalWithoutCommentsRate = 0.1,
                PrsPerWeek = 10, AveragePrSizeLines = 150, LargePrRate = 0.1,
                StalePrCount = 0
            },
            Commits = new CommitMetrics
            {
                AverageCommitSize = 50, ConventionalCommitCompliance = 0.8,
                CommitsPerDayAverage = 5, MergeCommitRate = 0.2, ForcePushCount = 0
            },
            Knowledge = new KnowledgeMetrics
            {
                BusFactor = 3, TopContributorConcentration = 0.4,
                ActiveContributors30d = 5, CommitsByAuthor = new(),
                FileOwnership = new(), SingleOwnerFileCount = 10,
                SingleOwnerFilePercent = 0.1
            }
        };
    }

    internal static AIInteractionQuality CreateAIInteractionQuality(
        double averageEffectiveScore = 0.5, double averageEfficiency = 0.5,
        double firstPassSuccessRate = 0.5, double contextLeverage = 0.5,
        double domainSignalEffectiveness = 0.5, double improvementTrend = 0.5)
    {
        return new AIInteractionQuality
        {
            AverageEffectiveScore = averageEffectiveScore,
            AverageEfficiency = averageEfficiency,
            FirstPassSuccessRate = firstPassSuccessRate,
            ContextLeverage = contextLeverage,
            DomainSignalEffectiveness = domainSignalEffectiveness,
            ImprovementTrend = improvementTrend,
            Usage = new AIUsageProfile
            {
                TotalInteractions = 10, AverageIterations = 1.5,
                InteractionsByCategory = new(), MostEffectiveCategory = "Refactor",
                LeastEffectiveCategory = "Debug", TokenEfficiencyOverall = 0.8,
                TopCorrections = []
            },
            ScoreByCategory = new(),
            InputLeverage = new(),
            ScoreTrend = []
        };
    }

    internal static RequirementsQuality CreateRequirementsQuality(
        double clarity = 0.5, double completeness = 0.5,
        double testability = 0.5, double atomicity = 0.5,
        double acceptanceCriteriaQuality = 0.5, double traceabilityToCode = 0.5)
    {
        return new RequirementsQuality
        {
            Clarity = clarity,
            Completeness = completeness,
            Testability = testability,
            Atomicity = atomicity,
            AcceptanceCriteriaQuality = acceptanceCriteriaQuality,
            TraceabilityToCode = traceabilityToCode,
            Stories = []
        };
    }

    internal static DeveloperExperience CreateDeveloperExperience(
        double buildTimeSatisfaction = 0.5, double testRunSpeed = 0.5,
        double onboardingFriction = 0.5, double toolingMaturity = 0.5,
        double innerLoopSpeed = 0.5, double debugExperience = 0.5)
    {
        return new DeveloperExperience
        {
            BuildTimeSatisfaction = buildTimeSatisfaction,
            TestRunSpeed = testRunSpeed,
            OnboardingFriction = onboardingFriction,
            ToolingMaturity = toolingMaturity,
            InnerLoopSpeed = innerLoopSpeed,
            DebugExperience = debugExperience,
            Tooling = new ToolingProfile
            {
                EditorConfigPresent = true, LaunchSettingsPresent = true,
                DirectoryBuildPropsPresent = false, DockerfilePresent = false,
                DevContainerPresent = false, GitHooksConfigured = false,
                ConfiguredTools = []
            },
            InnerLoop = new InnerLoopProfile
            {
                CleanBuildTime = TimeSpan.FromSeconds(30),
                IncrementalBuildTime = TimeSpan.FromSeconds(5),
                UnitTestRunTime = TimeSpan.FromSeconds(10),
                HotReloadSupported = true, WatchModeAvailable = true,
                StepsToFirstRun = 3
            }
        };
    }

    internal static CodeQuality CreateCodeQuality(double score = 0.5)
    {
        return new CodeQuality
        {
            MaintainabilityIndex = score,
            CyclomaticComplexity = score,
            CodeDuplication = score,
            StyleConsistency = score,
            NullSafety = score,
            ErrorHandling = score,
            Readability = score,
            Complexity = new ComplexityProfile
            {
                AverageCyclomaticComplexity = 5, MaxCyclomaticComplexity = 15,
                MethodsOverThreshold = 2, AverageLinesPerMethod = 20,
                MaxLinesPerMethod = 100, AverageNestingDepth = 2,
                MaxNestingDepth = 5, GodClasses = 0, LongMethods = 1,
                HotSpots = []
            },
            Style = new StyleAnalysis
            {
                TotalWarnings = 5, TotalErrors = 0, AnalyzerCompliance = 0.9,
                WarningsByRule = new(), EditorConfigPresent = true,
                GlobalSuppressionsMinimal = true
            },
            Smells = new CodeSmellSummary
            {
                TotalSmells = 3, SmellsByCategory = new(), TopSmells = []
            }
        };
    }

    internal static DependencyHealth CreateDependencyHealth(double score = 0.5)
    {
        return new DependencyHealth
        {
            Freshness = score, VulnerabilityFreedom = score,
            LicenseCompliance = score, TransitiveCleanliness = score,
            PackageCountScore = score, Packages = [], Vulnerabilities = [],
            Deprecated = []
        };
    }

    internal static SecurityPosture CreateSecurityPosture(double score = 0.5)
    {
        return new SecurityPosture
        {
            SecretHygiene = score, AuthPatterns = score,
            InputValidation = score, CryptographyPractice = score,
            DependencySecurity = score, HttpSecurity = score,
            DataProtection = score, Findings = [],
            Owasp = new OwaspCoverage
            {
                BrokenAccessControl = score, CryptographicFailures = score,
                Injection = score, InsecureDesign = score,
                SecurityMisconfiguration = score, VulnerableComponents = score,
                AuthenticationFailures = score, DataIntegrityFailures = score,
                LoggingMonitoringFailures = score, ServerSideRequestForgery = score
            }
        };
    }

    internal static Observability CreateObservability(double score = 0.5)
    {
        return new Observability
        {
            LoggingCoverage = score, LoggingQuality = score,
            MetricsInstrumentation = score, TracingCoverage = score,
            HealthCheckCoverage = score, AlertingReadiness = score,
            Logging = new LoggingProfile
            {
                Framework = "Serilog", StructuredLogging = true,
                CorrelationIdPropagation = false, LogStatementsCount = 50,
                StatementsByLevel = new(), SensitiveDataFiltering = 0.8
            },
            Metrics = new MetricsProfile
            {
                Framework = "OpenTelemetry", CustomMetricsCount = 5,
                MetricTypes = [], BusinessMetricsDefined = false
            },
            Tracing = new TracingProfile
            {
                Framework = "OpenTelemetry", DistributedTracingEnabled = false,
                InstrumentedEndpoints = 0, TotalEndpoints = 10,
                SpanAttributesRich = false
            }
        };
    }

    internal static PerformanceProfile CreatePerformanceProfile(double score = 0.5)
    {
        return new PerformanceProfile
        {
            StartupPerformance = score, MemoryEfficiency = score,
            ResponseLatency = score, ThroughputCapacity = score,
            AllocationEfficiency = score, BundleSize = score,
            Startup = new StartupMetrics
            {
                ColdStartTime = TimeSpan.FromSeconds(3),
                WarmStartTime = TimeSpan.FromSeconds(1),
                PeakStartupMemoryBytes = 100_000_000, AssembliesLoaded = 50
            },
            Runtime = new RuntimeMetrics
            {
                WorkingSetBytes = 200_000_000,
                Gen0CollectionsPerMinute = 10, Gen1CollectionsPerMinute = 2,
                Gen2CollectionsPerMinute = 0.1, AllocatedBytesPerSecond = 1_000_000,
                ThreadPoolUtilization = 0.3
            },
            Benchmarks = []
        };
    }

    internal static ProjectStructure CreateProjectStructure(double score = 0.5)
    {
        return new ProjectStructure
        {
            ProjectCount = 5, SourceFiles = 50, TotalLines = 5000,
            NamingConsistency = score, FolderOrganization = score,
            SolutionHygiene = score, ProjectGranularity = score,
            Projects = []
        };
    }

    internal static Architecture CreateArchitecture(double score = 0.5, int circularDeps = 0)
    {
        return new Architecture
        {
            PrimaryPattern = ArchitecturePattern.CleanArchitecture,
            LayerSeparation = score, DependencyDirection = score,
            CouplingScore = score, CohesionScore = score,
            AbstractionLevel = score, CircularDependencies = circularDeps,
            Patterns = []
        };
    }

    internal static Codebase CreateCodebase(
        double qualityScore = -1, double testingScore = -1,
        double overallScore = -1)
    {
        // When overallScore is set, use it for all dimensions
        // When individual scores are set, use those; otherwise default to 0.5
        double s = overallScore >= 0 ? overallScore : 0.5;
        double q = overallScore >= 0 ? overallScore : (qualityScore >= 0 ? qualityScore : 0.5);
        double t = overallScore >= 0 ? overallScore : (testingScore >= 0 ? testingScore : 0.5);

        return new Codebase
        {
            Name = "TestCodebase",
            RepositoryUrl = "https://github.com/test/repo",
            SnapshotDate = DateTime.UtcNow,
            Stack = new DotNetStack
            {
                TargetFramework = "net10.0", LanguageVersion = "13.0",
                NullableEnabled = true, ImplicitUsings = true,
                AotCompatible = false, SdkType = "Microsoft.NET.Sdk",
                GlobalUsings = []
            },
            Architecture = CreateArchitecture(s),
            Structure = CreateProjectStructure(s),
            Quality = CreateCodeQuality(q),
            Testing = CreateTestingStrategy(t, t, t, t, t),
            Dependencies = CreateDependencyHealth(s),
            Security = CreateSecurityPosture(s),
            Observability = CreateObservability(s),
            Pipeline = CreateCiCdPipeline(s, s, s, s, s, s),
            Documentation = CreateDocumentation(s, s, s, s, s, s),
            DevEx = CreateDeveloperExperience(s, s, s, s, s, s),
            Performance = CreatePerformanceProfile(s),
            Requirements = CreateRequirementsQuality(s, s, s, s, s, s),
            Process = CreateTeamProcess(s, s, s, s, s, s),
            AIQuality = CreateAIInteractionQuality(s, s, s, s, s, s)
        };
    }
}
