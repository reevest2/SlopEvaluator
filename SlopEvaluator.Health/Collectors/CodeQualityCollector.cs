using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Scans .cs files using Roslyn to produce a CodeQuality measurement.
/// </summary>
public class CodeQualityCollector
{
    private readonly CodeQualityOptions _options;
    private readonly ILogger<CodeQualityCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeQualityCollector"/> class.
    /// </summary>
    /// <param name="options">Optional quality thresholds; defaults to <see cref="CodeQualityOptions"/>.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public CodeQualityCollector(CodeQualityOptions? options = null, ILogger<CodeQualityCollector>? logger = null)
    {
        _options = options ?? new CodeQualityOptions();
        _logger = logger ?? NullLogger<CodeQualityCollector>.Instance;
    }

    /// <summary>
    /// Scan all .cs files under a directory and produce a CodeQuality score.
    /// </summary>
    public async Task<CodeQuality> CollectAsync(string projectPath)
    {
        var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => {
                var rel = Path.GetRelativePath(projectPath, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
                    && !rel.Contains("benchmarks/")
                    && !rel.Contains(".claude/worktrees/");
            })
            .ToList();

        _logger.LogInformation("Code quality scan found {FileCount} C# files to analyze", csFiles.Count);

        var allMethods = new List<MethodMetrics>();
        var allClasses = new List<ClassMetrics>();
        var allSmells = new List<CodeSmell>();
        int totalLines = 0;
        int xmlDocCount = 0;
        int publicMemberCount = 0;
        int nullableAnnotated = 0;
        int nullableTotal = 0;

        // Check project-level nullable context from .csproj files
        bool hasNullable = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
            .Any(csproj => File.ReadAllText(csproj).Contains("<Nullable>enable"));

        foreach (var file in csFiles)
        {
            var source = await File.ReadAllTextAsync(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync();
            var relativePath = Path.GetRelativePath(projectPath, file);

            totalLines += source.Split('\n').Length;

            // Check for per-file nullable enable
            if (source.Contains("#nullable enable"))
                hasNullable = true;

            var walker = new MetricsWalker(relativePath);
            walker.Visit(root);

            allMethods.AddRange(walker.Methods);
            allClasses.AddRange(walker.Classes);
            xmlDocCount += walker.XmlDocCount;
            publicMemberCount += walker.PublicMemberCount;
            nullableAnnotated += walker.NullableAnnotatedParams;
            nullableTotal += walker.TotalNullableParams;
        }

        // Detect smells from aggregated data
        foreach (var cls in allClasses)
        {
            if (cls.LineCount > _options.GodClassThreshold)
                allSmells.Add(new CodeSmell
                {
                    Category = "LargeClass",
                    FilePath = cls.FilePath,
                    Location = cls.Name,
                    Description = $"Class has {cls.LineCount} lines (threshold: {_options.GodClassThreshold})",
                    Severity = "warning"
                });
        }

        foreach (var method in allMethods)
        {
            if (method.LineCount > _options.LongMethodThreshold)
                allSmells.Add(new CodeSmell
                {
                    Category = "LongMethod",
                    FilePath = method.FilePath,
                    Location = method.Name,
                    Description = $"Method has {method.LineCount} lines (threshold: {_options.LongMethodThreshold})",
                    Severity = "warning"
                });

            if (method.NestingDepth > _options.DeepNestingThreshold)
                allSmells.Add(new CodeSmell
                {
                    Category = "DeepNesting",
                    FilePath = method.FilePath,
                    Location = method.Name,
                    Description = $"Nesting depth {method.NestingDepth} (threshold: {_options.DeepNestingThreshold})",
                    Severity = "warning"
                });

            if (method.ParameterCount > _options.TooManyParametersThreshold)
                allSmells.Add(new CodeSmell
                {
                    Category = "TooManyParameters",
                    FilePath = method.FilePath,
                    Location = method.Name,
                    Description = $"Method has {method.ParameterCount} parameters (threshold: {_options.TooManyParametersThreshold})",
                    Severity = "info"
                });
        }

        _logger.LogInformation("Detected {SmellCount} code smells across {MethodCount} methods", allSmells.Count, allMethods.Count);

        // Compute scores
        var complexityProfile = BuildComplexityProfile(allMethods, allClasses);
        var styleAnalysis = BuildStyleAnalysis(csFiles, projectPath);
        var smellSummary = BuildSmellSummary(allSmells);

        double avgCC = allMethods.Count > 0 ? allMethods.Average(m => m.CyclomaticComplexity) : 0;
        double maxCC = allMethods.Count > 0 ? allMethods.Max(m => m.CyclomaticComplexity) : 0;

        int highComplexityCount = allMethods.Count(m => m.CyclomaticComplexity > _options.ComplexityThreshold);
        if (highComplexityCount > 0)
            _logger.LogWarning("{Count} methods exceed cyclomatic complexity threshold of {Threshold}", highComplexityCount, _options.ComplexityThreshold);

        _logger.LogInformation("Code quality scores — Maintainability: {Maintainability:F3}, Complexity: {Complexity:F3}", ComputeMaintainabilityScore(allMethods, totalLines), NormalizeComplexity(avgCC));

        return new CodeQuality
        {
            MaintainabilityIndex = ComputeMaintainabilityScore(allMethods, totalLines),
            CyclomaticComplexity = NormalizeComplexity(avgCC),
            CodeDuplication = 1.0, // would need a duplication detector — stub at 1.0 for now
            StyleConsistency = styleAnalysis.AnalyzerCompliance,
            NullSafety = ComputeNullSafetyScore(hasNullable, nullableAnnotated, nullableTotal),
            ErrorHandling = ComputeErrorHandlingScore(allMethods),
            Readability = ComputeReadabilityScore(allMethods),
            Complexity = complexityProfile,
            Style = styleAnalysis,
            Smells = smellSummary
        };
    }

    /// <summary>
    /// Scan a single C# source string (for testing).
    /// </summary>
    public CodeQuality CollectFromSource(string source, string fileName = "Test.cs")
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var walker = new MetricsWalker(fileName);
        walker.Visit(root);

        var allSmells = new List<CodeSmell>();
        foreach (var cls in walker.Classes)
        {
            if (cls.LineCount > _options.GodClassThreshold)
                allSmells.Add(new CodeSmell
                {
                    Category = "LargeClass", FilePath = fileName, Location = cls.Name,
                    Description = $"Class has {cls.LineCount} lines", Severity = "warning"
                });
        }
        foreach (var method in walker.Methods)
        {
            if (method.LineCount > _options.LongMethodThreshold)
                allSmells.Add(new CodeSmell
                {
                    Category = "LongMethod", FilePath = fileName, Location = method.Name,
                    Description = $"Method has {method.LineCount} lines", Severity = "warning"
                });
            if (method.NestingDepth > _options.DeepNestingThreshold)
                allSmells.Add(new CodeSmell
                {
                    Category = "DeepNesting", FilePath = fileName, Location = method.Name,
                    Description = $"Nesting depth {method.NestingDepth}", Severity = "warning"
                });
        }

        int totalLines = source.Split('\n').Length;
        double avgCC = walker.Methods.Count > 0 ? walker.Methods.Average(m => m.CyclomaticComplexity) : 0;
        bool hasNullable = source.Contains("#nullable enable");

        return new CodeQuality
        {
            MaintainabilityIndex = ComputeMaintainabilityScore(walker.Methods, totalLines),
            CyclomaticComplexity = NormalizeComplexity(avgCC),
            CodeDuplication = 1.0,
            StyleConsistency = 1.0,
            NullSafety = ComputeNullSafetyScore(hasNullable, walker.NullableAnnotatedParams, walker.TotalNullableParams),
            ErrorHandling = ComputeErrorHandlingScore(walker.Methods),
            Readability = ComputeReadabilityScore(walker.Methods),
            Complexity = BuildComplexityProfile(walker.Methods, walker.Classes),
            Style = new StyleAnalysis
            {
                TotalWarnings = 0, TotalErrors = 0, AnalyzerCompliance = 1.0,
                WarningsByRule = new Dictionary<string, int>(),
                EditorConfigPresent = false, GlobalSuppressionsMinimal = true
            },
            Smells = BuildSmellSummary(allSmells)
        };
    }

    // --- Scoring functions ---

    /// <summary>
    /// Cyclomatic complexity → 0-1 score. Lower complexity = higher score.
    /// CC 1-5 = 1.0, CC 10 = 0.7, CC 20 = 0.3, CC 30+ = 0.0
    /// </summary>
    internal static double NormalizeComplexity(double avgCC)
    {
        if (avgCC <= 5) return 1.0;
        if (avgCC >= 30) return 0.0;
        return 1.0 - (avgCC - 5) / 25.0;
    }

    internal static double ComputeMaintainabilityScore(List<MethodMetrics> methods, int totalLines)
    {
        if (methods.Count == 0) return 1.0;

        double avgMethodLength = methods.Average(m => m.LineCount);
        double avgCC = methods.Average(m => m.CyclomaticComplexity);

        // Simplified maintainability: penalize long methods and high complexity
        double lengthScore = avgMethodLength <= 15 ? 1.0
            : avgMethodLength >= 60 ? 0.0
            : 1.0 - (avgMethodLength - 15) / 45.0;

        double ccScore = NormalizeComplexity(avgCC);

        return (lengthScore + ccScore) / 2.0;
    }

    internal static double ComputeReadabilityScore(List<MethodMetrics> methods)
    {
        if (methods.Count == 0) return 1.0;

        double avgNesting = methods.Average(m => m.NestingDepth);
        double avgLength = methods.Average(m => m.LineCount);

        // Nesting: 0-2 = 1.0, 3 = 0.7, 4 = 0.4, 5+ = 0.0
        double nestingScore = avgNesting <= 2 ? 1.0
            : avgNesting >= 5 ? 0.0
            : 1.0 - (avgNesting - 2) / 3.0;

        // Length: < 20 = 1.0, 20-40 = linear, > 40 = 0.0
        double lengthScore = avgLength <= 20 ? 1.0
            : avgLength >= 40 ? 0.0
            : 1.0 - (avgLength - 20) / 20.0;

        return (nestingScore * 0.6 + lengthScore * 0.4);
    }

    internal static double ComputeErrorHandlingScore(List<MethodMetrics> methods)
    {
        if (methods.Count == 0) return 1.0;

        int methodsWithTryCatch = methods.Count(m => m.HasTryCatch);
        int methodsWithThrow = methods.Count(m => m.HasThrow);
        int total = methods.Count;

        // Having some error handling is good, but not every method needs try/catch
        // Score based on: methods that throw or catch / total, capped at reasonable ratio
        double handlingRatio = (double)(methodsWithTryCatch + methodsWithThrow) / total;

        // Sweet spot: 10-40% of methods have error handling
        if (handlingRatio < 0.05) return 0.3;  // almost no error handling
        if (handlingRatio <= 0.40) return 1.0;  // good ratio
        return Math.Max(0.5, 1.0 - (handlingRatio - 0.40) * 2); // over-catching
    }

    internal static double ComputeNullSafetyScore(bool hasNullable, int annotated, int total)
    {
        if (!hasNullable) return 0.0;
        if (total == 0) return 1.0;
        // In a nullable-enabled project, non-nullable params are the safe ones.
        // Score = fraction of params that are non-nullable (not annotated with ?).
        // Base 0.5 for having nullable enabled + 0.5 scaled by non-nullable ratio.
        double nonNullableRatio = 1.0 - (double)annotated / total;
        return 0.5 + 0.5 * nonNullableRatio;
    }

    // --- Profile builders ---

    private ComplexityProfile BuildComplexityProfile(List<MethodMetrics> methods, List<ClassMetrics> classes)
    {
        if (methods.Count == 0)
            return new ComplexityProfile
            {
                AverageCyclomaticComplexity = 0, MaxCyclomaticComplexity = 0,
                MethodsOverThreshold = 0, AverageLinesPerMethod = 0, MaxLinesPerMethod = 0,
                AverageNestingDepth = 0, MaxNestingDepth = 0, GodClasses = 0, LongMethods = 0,
                HotSpots = []
            };

        return new ComplexityProfile
        {
            AverageCyclomaticComplexity = methods.Average(m => m.CyclomaticComplexity),
            MaxCyclomaticComplexity = methods.Max(m => m.CyclomaticComplexity),
            MethodsOverThreshold = methods.Count(m => m.CyclomaticComplexity > _options.ComplexityThreshold),
            AverageLinesPerMethod = methods.Average(m => m.LineCount),
            MaxLinesPerMethod = methods.Max(m => m.LineCount),
            AverageNestingDepth = methods.Average(m => m.NestingDepth),
            MaxNestingDepth = methods.Max(m => m.NestingDepth),
            GodClasses = classes.Count(c => c.LineCount > _options.GodClassThreshold),
            LongMethods = methods.Count(m => m.LineCount > _options.LongMethodThreshold),
            HotSpots = methods
                .OrderByDescending(m => m.CyclomaticComplexity)
                .Take(10)
                .Select(m => new HotSpot
                {
                    FilePath = m.FilePath,
                    MethodName = m.Name,
                    CyclomaticComplexity = m.CyclomaticComplexity,
                    LineCount = m.LineCount,
                    NestingDepth = m.NestingDepth
                })
                .ToList()
        };
    }

    private StyleAnalysis BuildStyleAnalysis(List<string> csFiles, string projectPath)
    {
        bool editorConfig = File.Exists(Path.Combine(projectPath, ".editorconfig"));

        return new StyleAnalysis
        {
            TotalWarnings = 0,  // would come from dotnet build output
            TotalErrors = 0,
            AnalyzerCompliance = 1.0,  // default — override with build results
            WarningsByRule = new Dictionary<string, int>(),
            EditorConfigPresent = editorConfig,
            GlobalSuppressionsMinimal = true
        };
    }

    private static CodeSmellSummary BuildSmellSummary(List<CodeSmell> smells)
    {
        return new CodeSmellSummary
        {
            TotalSmells = smells.Count,
            SmellsByCategory = smells.GroupBy(s => s.Category)
                .ToDictionary(g => g.Key, g => g.Count()),
            TopSmells = smells.Take(20).ToList()
        };
    }
}

/// <summary>
/// Configuration thresholds for code quality detection.
/// </summary>
public class CodeQualityOptions
{
    /// <summary>Cyclomatic complexity threshold above which a method is flagged.</summary>
    public int ComplexityThreshold { get; init; } = 10;

    /// <summary>Line count threshold above which a method is considered too long.</summary>
    public int LongMethodThreshold { get; init; } = 50;

    /// <summary>Line count threshold above which a class is flagged as a god class.</summary>
    public int GodClassThreshold { get; init; } = 500;

    /// <summary>Nesting depth threshold above which a method is flagged for deep nesting.</summary>
    public int DeepNestingThreshold { get; init; } = 4;

    /// <summary>Parameter count threshold above which a method is flagged.</summary>
    public int TooManyParametersThreshold { get; init; } = 5;
}

// --- Internal data structures ---

internal record MethodMetrics
{
    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required int CyclomaticComplexity { get; init; }
    public required int LineCount { get; init; }
    public required int NestingDepth { get; init; }
    public required int ParameterCount { get; init; }
    public required bool HasTryCatch { get; init; }
    public required bool HasThrow { get; init; }
}

internal record ClassMetrics
{
    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required int LineCount { get; init; }
    public required int MethodCount { get; init; }
}
