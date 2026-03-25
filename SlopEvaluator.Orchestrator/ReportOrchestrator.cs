using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Analysis;
using SlopEvaluator.Health.Models;
using SlopEvaluator.Mutations.Generators;

namespace SlopEvaluator.Orchestrator;

/// <summary>
/// Generates a diagnostic report with actionable recommendations.
/// Scans health, identifies weak spots, and counts mutation candidates — all read-only.
/// </summary>
public sealed class ReportOrchestrator
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportOrchestrator"/> class.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory; defaults to <see cref="NullLoggerFactory"/>.</param>
    public ReportOrchestrator(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Generate a diagnostic report with actionable recommendations.
    /// </summary>
    /// <param name="path">Path to the project root.</param>
    /// <param name="name">Optional codebase name; auto-detected from directory name if null.</param>
    /// <param name="maxTargets">Maximum number of weak files to analyze.</param>
    /// <returns>A <see cref="ReportResult"/> containing dimension advice, file analyses, and coverage gaps.</returns>
    public async Task<ReportResult> GenerateAsync(string path, string? name, int maxTargets = 5)
    {
        path = Path.GetFullPath(path);
        name ??= Path.GetFileName(path);

        // Step 1: Run health scan
        var scanner = new ScanOrchestrator(_loggerFactory);
        var (codebase, delta) = await scanner.ScanAsync(path, name);

        // Step 2: Get all 14 dimension scores
        var analyzer = new SnapshotAnalyzer(_loggerFactory.CreateLogger<SnapshotAnalyzer>());
        var dimensionScores = analyzer.GetDimensionScores(codebase);

        // Step 3: Identify weak dimensions (score < 0.70)
        var weakDimensions = new List<DimensionAdvice>();
        foreach (var kv in dimensionScores.OrderBy(kv => kv.Value))
        {
            if (kv.Value < 0.70)
            {
                weakDimensions.Add(GetDimensionAdvice(kv.Key, kv.Value, codebase));
            }
        }

        // Step 4: Select weak files for mutation analysis
        var selector = new TargetSelector();
        var targets = selector.SelectTargets(codebase, path, maxTargets);

        // Step 5: For each target, count mutation candidates
        var fileAnalyses = new List<FileAnalysis>();
        foreach (var target in targets)
        {
            int candidateCount = 0;
            var strategyBreakdown = new List<string>();

            try
            {
                var generator = new RoslynMutationGenerator(target.FilePath, _ => { });
                var config = generator.Generate("dotnet test", projectPath: path, maxMutations: 200);
                candidateCount = config.Mutations.Count;

                // Group by strategy name
                var groups = config.Mutations
                    .GroupBy(m => m.Strategy)
                    .OrderByDescending(g => g.Count());
                foreach (var g in groups)
                {
                    strategyBreakdown.Add($"{g.Key}: {g.Count()}");
                }
            }
            catch
            {
                // File may not parse or have other issues — skip silently
            }

            var relativePath = Path.GetRelativePath(path, target.FilePath);
            fileAnalyses.Add(new FileAnalysis(
                FilePath: relativePath,
                WeaknessScore: target.WeaknessScore,
                Reason: target.Reason,
                MutationCandidateCount: candidateCount,
                StrategyBreakdown: strategyBreakdown,
                SuggestedCommand: $"slop mutate \"{relativePath}\" --roslyn"));
        }

        // Step 6: Detect coverage gaps
        var coverageGaps = DetectCoverageGaps(path);

        return new ReportResult
        {
            Codebase = codebase,
            Delta = delta,
            WeakDimensions = weakDimensions,
            FileAnalyses = fileAnalyses,
            CoverageGaps = coverageGaps
        };
    }

    /// <summary>
    /// Detect source files that have mutation candidates but no matching test files.
    /// </summary>
    private List<MutationCoverageGap> DetectCoverageGaps(string projectPath)
    {
        var gaps = new List<MutationCoverageGap>();

        // Find all source and test files
        var allCsFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var normalized = f.Replace('\\', '/');
                return !normalized.Contains("/obj/")
                    && !normalized.Contains("/bin/")
                    && !normalized.Contains("/node_modules/")
                    && !normalized.Contains(".g.cs")
                    && !normalized.Contains(".designer.cs", StringComparison.OrdinalIgnoreCase)
                    && !normalized.Contains("GlobalUsings")
                    && !normalized.Contains("AssemblyInfo");
            })
            .ToList();

        var testFiles = allCsFiles.Where(f =>
        {
            var fileName = Path.GetFileNameWithoutExtension(f);
            var dir = Path.GetDirectoryName(f)?.Replace('\\', '/') ?? "";
            return fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
                || dir.Contains("/Test", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        var sourceFiles = allCsFiles.Except(testFiles).ToList();

        var testFileNames = testFiles
            .Select(f => Path.GetFileNameWithoutExtension(f)
                .Replace("Tests", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Test", "", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sourceFiles)
        {
            var sourceName = Path.GetFileNameWithoutExtension(source);

            // Try to count mutation candidates
            int candidateCount = 0;
            try
            {
                var generator = new RoslynMutationGenerator(source, _ => { });
                var config = generator.Generate("dotnet test", projectPath: projectPath, maxMutations: 200);
                candidateCount = config.Mutations.Count;
            }
            catch
            {
                continue; // Skip files that fail to parse
            }

            if (candidateCount == 0) continue;

            bool hasMatch = testFileNames.Contains(sourceName);
            string? matchingTestFile = hasMatch
                ? testFiles.FirstOrDefault(t =>
                {
                    var tName = Path.GetFileNameWithoutExtension(t);
                    return tName.Equals(sourceName + "Tests", StringComparison.OrdinalIgnoreCase)
                        || tName.Equals(sourceName + "Test", StringComparison.OrdinalIgnoreCase);
                })
                : null;

            // Gap severity: higher when more candidates and no test file
            double gapSeverity = hasMatch
                ? Math.Min(1.0, candidateCount / 50.0) * 0.3
                : Math.Min(1.0, candidateCount / 30.0);

            if (gapSeverity > 0.2)
            {
                var relativePath = Path.GetRelativePath(projectPath, source);
                var relativeTestPath = matchingTestFile is not null
                    ? Path.GetRelativePath(projectPath, matchingTestFile)
                    : null;

                gaps.Add(new MutationCoverageGap(
                    SourceFile: relativePath,
                    CandidateCount: candidateCount,
                    HasMatchingTestFile: hasMatch,
                    TestFilePath: relativeTestPath,
                    GapSeverity: gapSeverity));
            }
        }

        return gaps.OrderByDescending(g => g.GapSeverity).ToList();
    }

    private static DimensionAdvice GetDimensionAdvice(string name, double score, Codebase cb)
    {
        return name switch
        {
            "CodeQuality" => GetCodeQualityAdvice(score, cb),
            "Testing" => GetTestingAdvice(score, cb),
            "Dependencies" => GetDependenciesAdvice(score, cb),
            "Security" => GetSecurityAdvice(score, cb),
            "Observability" => GetObservabilityAdvice(score, cb),
            "CiCd" => GetCiCdAdvice(score, cb),
            "Documentation" => GetDocumentationAdvice(score, cb),
            "DevExperience" => GetDevExperienceAdvice(score, cb),
            "Performance" => GetPerformanceAdvice(score, cb),
            "Requirements" => GetRequirementsAdvice(score, cb),
            "TeamProcess" => GetTeamProcessAdvice(score, cb),
            "AIQuality" => GetAIQualityAdvice(score, cb),
            "Architecture" => GetArchitectureAdvice(score, cb),
            "Structure" => GetStructureAdvice(score, cb),
            _ => new DimensionAdvice(name, score, "Unknown dimension", [])
        };
    }

    private static DimensionAdvice GetCodeQualityAdvice(double score, Codebase cb)
    {
        var q = cb.Quality;
        var weakest = new (string Name, double Score)[]
        {
            ("CyclomaticComplexity", q.CyclomaticComplexity),
            ("MaintainabilityIndex", q.MaintainabilityIndex),
            ("CodeDuplication", q.CodeDuplication),
            ("StyleConsistency", q.StyleConsistency),
            ("NullSafety", q.NullSafety),
            ("ErrorHandling", q.ErrorHandling),
            ("Readability", q.Readability)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "CyclomaticComplexity" => "Reduce method complexity by extracting helper methods",
                "MaintainabilityIndex" => "Improve maintainability: shorten methods and reduce nesting",
                "CodeDuplication" => "Eliminate code duplication with shared abstractions",
                "StyleConsistency" => "Enable and enforce .editorconfig rules",
                "NullSafety" => "Enable nullable reference types and fix warnings",
                "ErrorHandling" => "Add structured error handling with specific exception types",
                "Readability" => "Improve naming and add XML doc comments",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("CodeQuality", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetTestingAdvice(double score, Codebase cb)
    {
        var t = cb.Testing;
        var weakest = new (string Name, double Score)[]
        {
            ("LineCoverage", t.LineCoverage),
            ("BranchCoverage", t.BranchCoverage),
            ("MutationScore", t.MutationScore),
            ("EdgeCaseCoverage", t.EdgeCaseCoverage),
            ("TestQualityScore", t.TestQualityScore)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "LineCoverage" => $"Increase line coverage above 80% (currently {t.LineCoverage:P0})",
                "BranchCoverage" => $"Increase branch coverage above 60% (currently {t.BranchCoverage:P0})",
                "MutationScore" => $"Run mutation testing on high-value files (currently {t.MutationScore:P0})",
                "EdgeCaseCoverage" => "Add tests for boundary values, nulls, and empty collections",
                "TestQualityScore" => "Improve test precision with specific assertions",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Testing", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetDependenciesAdvice(double score, Codebase cb)
    {
        var d = cb.Dependencies;
        var weakest = new (string Name, double Score)[]
        {
            ("Freshness", d.Freshness),
            ("VulnerabilityFreedom", d.VulnerabilityFreedom),
            ("LicenseCompliance", d.LicenseCompliance),
            ("TransitiveCleanliness", d.TransitiveCleanliness)
        }.OrderBy(x => x.Score).Take(2).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "Freshness" => "Update outdated NuGet packages to latest stable versions",
                "VulnerabilityFreedom" => "Resolve known security vulnerabilities in dependencies",
                "LicenseCompliance" => "Review and resolve license compatibility issues",
                "TransitiveCleanliness" => "Pin transitive dependencies and reduce dependency tree depth",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Dependencies", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetSecurityAdvice(double score, Codebase cb)
    {
        var s = cb.Security;
        var weakest = new (string Name, double Score)[]
        {
            ("SecretHygiene", s.SecretHygiene),
            ("AuthPatterns", s.AuthPatterns),
            ("InputValidation", s.InputValidation),
            ("CryptographyPractice", s.CryptographyPractice),
            ("DependencySecurity", s.DependencySecurity),
            ("HttpSecurity", s.HttpSecurity),
            ("DataProtection", s.DataProtection)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "SecretHygiene" => "Move secrets to user-secrets or environment variables",
                "AuthPatterns" => "Implement consistent authentication and authorization patterns",
                "InputValidation" => "Add input validation on all public API endpoints",
                "CryptographyPractice" => "Use modern cryptographic algorithms and key management",
                "DependencySecurity" => "Audit dependencies for known security vulnerabilities",
                "HttpSecurity" => "Enable HTTPS, HSTS, and security headers",
                "DataProtection" => "Encrypt sensitive data at rest and in transit",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Security", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetObservabilityAdvice(double score, Codebase cb)
    {
        var o = cb.Observability;
        var weakest = new (string Name, double Score)[]
        {
            ("LoggingCoverage", o.LoggingCoverage),
            ("LoggingQuality", o.LoggingQuality),
            ("MetricsInstrumentation", o.MetricsInstrumentation),
            ("TracingCoverage", o.TracingCoverage),
            ("HealthCheckCoverage", o.HealthCheckCoverage),
            ("AlertingReadiness", o.AlertingReadiness)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "LoggingCoverage" => "Add structured logging to all service entry points",
                "LoggingQuality" => "Use semantic log levels and include correlation IDs",
                "MetricsInstrumentation" => "Add custom metrics for key business operations",
                "TracingCoverage" => "Enable distributed tracing with OpenTelemetry",
                "HealthCheckCoverage" => "Implement health check endpoints for all dependencies",
                "AlertingReadiness" => "Configure alerting rules for critical metrics",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Observability", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetCiCdAdvice(double score, Codebase cb)
    {
        var p = cb.Pipeline;
        var weakest = new (string Name, double Score)[]
        {
            ("PipelineCompleteness", p.PipelineCompleteness),
            ("BuildReliability", p.BuildReliability),
            ("BuildSpeed", p.BuildSpeed),
            ("DeployFrequency", p.DeployFrequency),
            ("EnvironmentParity", p.EnvironmentParity),
            ("RollbackCapability", p.RollbackCapability)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "PipelineCompleteness" => "Add missing pipeline stages (security scan, integration tests)",
                "BuildReliability" => "Fix flaky builds and reduce build failures",
                "BuildSpeed" => "Enable build caching and incremental builds in CI",
                "DeployFrequency" => "Increase deployment frequency with automated releases",
                "EnvironmentParity" => "Align staging and production environments",
                "RollbackCapability" => "Implement automated rollback on deployment failures",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("CiCd", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetDocumentationAdvice(double score, Codebase cb)
    {
        var d = cb.Documentation;
        var weakest = new (string Name, double Score)[]
        {
            ("ReadmeCompleteness", d.ReadmeCompleteness),
            ("ApiDocCoverage", d.ApiDocCoverage),
            ("AdrPresence", d.AdrPresence),
            ("DocFreshness", d.DocFreshness),
            ("InlineCommentQuality", d.InlineCommentQuality),
            ("OnboardingDocumentation", d.OnboardingDocumentation)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "ReadmeCompleteness" => "Add setup instructions, architecture overview, and examples to README",
                "ApiDocCoverage" => "Add XML doc comments to all public members",
                "AdrPresence" => "Create Architecture Decision Records for key design choices",
                "DocFreshness" => "Review and update stale documentation",
                "InlineCommentQuality" => "Add meaningful comments explaining the 'why', not the 'what'",
                "OnboardingDocumentation" => "Create a getting-started guide for new developers",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Documentation", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetDevExperienceAdvice(double score, Codebase cb)
    {
        var dx = cb.DevEx;
        var weakest = new (string Name, double Score)[]
        {
            ("ToolingMaturity", dx.ToolingMaturity),
            ("DebugExperience", dx.DebugExperience),
            ("BuildTimeSatisfaction", dx.BuildTimeSatisfaction),
            ("TestRunSpeed", dx.TestRunSpeed),
            ("OnboardingFriction", dx.OnboardingFriction),
            ("InnerLoopSpeed", dx.InnerLoopSpeed)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "ToolingMaturity" => "Add .editorconfig, Directory.Build.props, and dev container support",
                "DebugExperience" => "Configure launch profiles and add diagnostic logging",
                "BuildTimeSatisfaction" => "Optimize build times with incremental compilation",
                "TestRunSpeed" => "Parallelize test execution and reduce integration test overhead",
                "OnboardingFriction" => "Reduce setup steps and document prerequisites clearly",
                "InnerLoopSpeed" => "Enable hot reload and watch mode for rapid iteration",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("DevExperience", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetPerformanceAdvice(double score, Codebase cb)
    {
        var p = cb.Performance;
        var weakest = new (string Name, double Score)[]
        {
            ("StartupPerformance", p.StartupPerformance),
            ("MemoryEfficiency", p.MemoryEfficiency),
            ("ResponseLatency", p.ResponseLatency),
            ("ThroughputCapacity", p.ThroughputCapacity),
            ("AllocationEfficiency", p.AllocationEfficiency),
            ("BundleSize", p.BundleSize)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "StartupPerformance" => "Reduce startup time with lazy loading and trimming",
                "MemoryEfficiency" => "Profile memory usage and reduce allocations in hot paths",
                "ResponseLatency" => "Optimize critical request paths and add caching",
                "ThroughputCapacity" => "Use async I/O and optimize thread pool usage",
                "AllocationEfficiency" => "Use Span<T>, ArrayPool, and value types to reduce GC pressure",
                "BundleSize" => "Enable trimming and remove unused assemblies",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Performance", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetRequirementsAdvice(double score, Codebase cb)
    {
        var r = cb.Requirements;
        var weakest = new (string Name, double Score)[]
        {
            ("Clarity", r.Clarity),
            ("Testability", r.Testability),
            ("AcceptanceCriteriaQuality", r.AcceptanceCriteriaQuality)
        }.OrderBy(x => x.Score).Take(2).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "Clarity" => "Write requirements with specific, measurable acceptance criteria",
                "Testability" => "Ensure each requirement maps to at least one test case",
                "AcceptanceCriteriaQuality" => "Use Given-When-Then format for acceptance criteria",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Requirements", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetTeamProcessAdvice(double score, Codebase cb)
    {
        var p = cb.Process;
        var weakest = new (string Name, double Score)[]
        {
            ("CommitHygiene", p.CommitHygiene),
            ("BranchStrategy", p.BranchStrategy),
            ("KnowledgeDistribution", p.KnowledgeDistribution)
        }.OrderBy(x => x.Score).Take(2).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "CommitHygiene" => "Use conventional commits and keep commits focused and atomic",
                "BranchStrategy" => "Adopt a consistent branching strategy (trunk-based or GitFlow)",
                "KnowledgeDistribution" => "Rotate code ownership and encourage cross-team reviews",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("TeamProcess", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetAIQualityAdvice(double score, Codebase cb)
    {
        var ai = cb.AIQuality;
        var weakest = new (string Name, double Score)[]
        {
            ("FirstPassSuccessRate", ai.FirstPassSuccessRate),
            ("AverageEffectiveScore", ai.AverageEffectiveScore)
        }.OrderBy(x => x.Score).Take(2).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "FirstPassSuccessRate" => "Improve prompt specificity and provide more context in AI interactions",
                "AverageEffectiveScore" => "Review and refine AI prompts for higher quality outputs",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("AIQuality", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetArchitectureAdvice(double score, Codebase cb)
    {
        var a = cb.Architecture;
        var weakest = new (string Name, double Score)[]
        {
            ("CouplingScore", a.CouplingScore),
            ("LayerSeparation", a.LayerSeparation),
            ("DependencyDirection", a.DependencyDirection),
            ("CohesionScore", a.CohesionScore),
            ("AbstractionLevel", a.AbstractionLevel)
        }.OrderBy(x => x.Score).Take(3).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();

        if (a.CircularDependencies > 0)
            actions.Add($"Resolve {a.CircularDependencies} circular dependency chain(s)");

        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "CouplingScore" => "Reduce coupling by introducing interfaces and dependency injection",
                "LayerSeparation" => "Enforce layer boundaries with project reference constraints",
                "DependencyDirection" => "Ensure dependencies point inward toward the domain layer",
                "CohesionScore" => "Group related functionality and split mixed-responsibility classes",
                "AbstractionLevel" => "Introduce appropriate abstractions for cross-cutting concerns",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Architecture", score,
            string.Join(", ", whyParts), actions);
    }

    private static DimensionAdvice GetStructureAdvice(double score, Codebase cb)
    {
        var s = cb.Structure;
        var weakest = new (string Name, double Score)[]
        {
            ("NamingConsistency", s.NamingConsistency),
            ("FolderOrganization", s.FolderOrganization),
            ("SolutionHygiene", s.SolutionHygiene),
            ("ProjectGranularity", s.ProjectGranularity)
        }.OrderBy(x => x.Score).Take(2).ToList();

        var whyParts = weakest.Select(w => $"{w.Name} at {w.Score:P0}");
        var actions = new List<string>();
        foreach (var w in weakest)
        {
            actions.Add(w.Name switch
            {
                "NamingConsistency" => "Standardize namespace and file naming conventions",
                "FolderOrganization" => "Organize files by feature or layer with consistent folder structure",
                "SolutionHygiene" => "Remove unused projects and clean up solution file references",
                "ProjectGranularity" => "Adjust project boundaries to match architectural layers",
                _ => $"Improve {w.Name}"
            });
        }

        return new DimensionAdvice("Structure", score,
            string.Join(", ", whyParts), actions);
    }
}

/// <summary>
/// Complete diagnostic report result with dimension advice, file analyses, and coverage gaps.
/// </summary>
public sealed record ReportResult
{
    /// <summary>The scanned codebase health data.</summary>
    public required Codebase Codebase { get; init; }

    /// <summary>Delta from previous snapshot, if available.</summary>
    public required SnapshotDelta? Delta { get; init; }

    /// <summary>Weak dimensions with explanations and recommended actions.</summary>
    public required List<DimensionAdvice> WeakDimensions { get; init; }

    /// <summary>File-level analyses with mutation candidate counts.</summary>
    public required List<FileAnalysis> FileAnalyses { get; init; }

    /// <summary>Source files with mutation candidates but no matching test files.</summary>
    public required List<MutationCoverageGap> CoverageGaps { get; init; }
}

/// <summary>
/// Advice for a single weak health dimension including explanation and actionable fixes.
/// </summary>
/// <param name="Name">Dimension name.</param>
/// <param name="Score">Current dimension score (0.0 to 1.0).</param>
/// <param name="WhyLow">Human-readable explanation of why the score is low.</param>
/// <param name="Actions">Ordered list of recommended actions to improve the score.</param>
public sealed record DimensionAdvice(
    string Name, double Score, string WhyLow, List<string> Actions);

/// <summary>
/// Analysis of a single file including weakness score and mutation candidate count.
/// </summary>
/// <param name="FilePath">Relative path to the source file.</param>
/// <param name="WeaknessScore">Weakness score from 0.0 (strongest) to 1.0 (weakest).</param>
/// <param name="Reason">Human-readable explanation of why this file was selected.</param>
/// <param name="MutationCandidateCount">Number of mutation candidates found.</param>
/// <param name="StrategyBreakdown">Mutation candidates grouped by strategy (e.g. "boundary: 8").</param>
/// <param name="SuggestedCommand">CLI command to mutate this file.</param>
public sealed record FileAnalysis(
    string FilePath, double WeaknessScore, string Reason,
    int MutationCandidateCount, List<string> StrategyBreakdown,
    string SuggestedCommand);

/// <summary>
/// A source file with mutation candidates that may lack adequate test coverage.
/// </summary>
/// <param name="SourceFile">Relative path to the source file.</param>
/// <param name="CandidateCount">Number of mutation candidates found.</param>
/// <param name="HasMatchingTestFile">Whether a test file matching the source file name was found.</param>
/// <param name="TestFilePath">Relative path to the matching test file, if any.</param>
/// <param name="GapSeverity">Severity of the coverage gap from 0.0 (minor) to 1.0 (critical).</param>
public sealed record MutationCoverageGap(
    string SourceFile, int CandidateCount,
    bool HasMatchingTestFile, string? TestFilePath,
    double GapSeverity);
