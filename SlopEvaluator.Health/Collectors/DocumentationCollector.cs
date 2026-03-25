using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Scans for README, API docs (XML comments), ADRs, and doc freshness.
/// </summary>
public class DocumentationCollector
{
    private readonly ILogger<DocumentationCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentationCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public DocumentationCollector(ILogger<DocumentationCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<DocumentationCollector>.Instance;
    }

    /// <summary>
    /// Scan README, API docs, ADRs, and doc freshness for a project.
    /// </summary>
    public async Task<Documentation> CollectAsync(string projectPath)
    {
        _logger.LogDebug("Starting documentation scan for {ProjectPath}", projectPath);
        var inventory = await BuildInventoryAsync(projectPath);
        _logger.LogDebug("Found {AdrCount} ADR documents in {ProjectPath}", inventory.AdrCount, projectPath);

        if (!HasFile(projectPath, "README.md") && !HasFile(projectPath, "readme.md"))
            _logger.LogWarning("README.md not found at root of {Path}", projectPath);

        double readmeCompleteness = ScoreReadmeCompleteness(projectPath);
        double apiDocCoverage = inventory.PublicMembersTotal > 0
            ? (double)inventory.PublicMembersDocumented / inventory.PublicMembersTotal
            : 1.0;
        _logger.LogInformation("API doc coverage: {Percentage:F1}% ({Documented}/{Total} public members documented)",
            apiDocCoverage * 100, inventory.PublicMembersDocumented, inventory.PublicMembersTotal);
        double adrPresence = inventory.AdrCount > 0 ? Math.Min(1.0, inventory.AdrCount / 5.0) : 0.0;
        double docFreshness = await ScoreDocFreshnessAsync(projectPath);
        double onboarding = ScoreOnboardingDocs(projectPath);

        _logger.LogInformation("README completeness: {ReadmeScore:F3}, exists: {ReadmeExists}", readmeCompleteness, inventory.ReadmeExists);

        return new Documentation
        {
            ReadmeCompleteness = readmeCompleteness,
            ApiDocCoverage = apiDocCoverage,
            AdrPresence = adrPresence,
            DocFreshness = docFreshness,
            InlineCommentQuality = 0.5, // would need NLP — stub at neutral
            OnboardingDocumentation = onboarding,
            Inventory = inventory
        };
    }

    private async Task<DocInventory> BuildInventoryAsync(string path)
    {
        bool readmeExists = HasFile(path, "README.md") || HasFile(path, "readme.md");
        bool changelogExists = HasFile(path, "CHANGELOG.md") || HasFile(path, "changelog.md");
        bool contributingExists = HasFile(path, "CONTRIBUTING.md") || HasFile(path, "contributing.md");
        bool claudeMdExists = File.Exists(Path.Combine(path, ".claude", "CLAUDE.md"))
            || HasFile(path, "CLAUDE.md");

        // Count ADRs
        int adrCount = 0;
        var adrDirs = new[] { "docs/adr", "docs/ADR", "adr", "ADR", "docs/decisions" };
        foreach (var dir in adrDirs)
        {
            var fullPath = Path.Combine(path, dir);
            if (Directory.Exists(fullPath))
                adrCount += Directory.GetFiles(fullPath, "*.md").Length;
        }

        // Scan public members and XML doc coverage
        int publicTotal = 0;
        int publicDocumented = 0;
        var docByProject = new Dictionary<string, double>();

        var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)
            .Where(f => {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
                    && !rel.Contains("benchmarks/")
                    && !rel.Contains(".claude/worktrees/");
            })
            .ToList();

        foreach (var csproj in csprojFiles)
        {
            var projDir = Path.GetDirectoryName(csproj)!;
            var projName = Path.GetFileNameWithoutExtension(csproj);
            int projPublic = 0, projDocumented = 0;

            var csFiles = Directory.GetFiles(projDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/obj/")
                         && !f.Replace('\\', '/').Contains("/bin/"));

            foreach (var file in csFiles)
            {
                var source = await File.ReadAllTextAsync(file);
                var tree = CSharpSyntaxTree.ParseText(source);
                var root = await tree.GetRootAsync();

                foreach (var member in root.DescendantNodes())
                {
                    bool isPublic = member switch
                    {
                        MethodDeclarationSyntax m => m.Modifiers.Any(SyntaxKind.PublicKeyword),
                        PropertyDeclarationSyntax p => p.Modifiers.Any(SyntaxKind.PublicKeyword),
                        ClassDeclarationSyntax c => c.Modifiers.Any(SyntaxKind.PublicKeyword),
                        RecordDeclarationSyntax r => r.Modifiers.Any(SyntaxKind.PublicKeyword),
                        InterfaceDeclarationSyntax i => i.Modifiers.Any(SyntaxKind.PublicKeyword),
                        EnumDeclarationSyntax e => e.Modifiers.Any(SyntaxKind.PublicKeyword),
                        _ => false
                    };

                    if (!isPublic) continue;
                    projPublic++;

                    bool hasXmlDoc = member.HasLeadingTrivia &&
                        member.GetLeadingTrivia().Any(t =>
                            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                            || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

                    if (hasXmlDoc) projDocumented++;
                }
            }

            publicTotal += projPublic;
            publicDocumented += projDocumented;
            if (projPublic > 0)
                docByProject[projName] = (double)projDocumented / projPublic;
        }

        DateTime? readmeLastModified = null;
        var readmePath = Path.Combine(path, "README.md");
        if (File.Exists(readmePath))
            readmeLastModified = File.GetLastWriteTimeUtc(readmePath);

        return new DocInventory
        {
            ReadmeExists = readmeExists,
            ChangelogExists = changelogExists,
            ContributingGuideExists = contributingExists,
            ClaudeMdExists = claudeMdExists,
            AdrCount = adrCount,
            PublicMembersTotal = publicTotal,
            PublicMembersDocumented = publicDocumented,
            ReadmeLastModified = readmeLastModified,
            MostRecentAdr = null,
            DocCoverageByProject = docByProject
        };
    }

    /// <summary>
    /// Score README completeness: does it have key sections?
    /// </summary>
    internal static double ScoreReadmeCompleteness(string path)
    {
        var readmePath = Path.Combine(path, "README.md");
        if (!File.Exists(readmePath))
            readmePath = Path.Combine(path, "readme.md");
        if (!File.Exists(readmePath))
            return 0.0;

        var content = File.ReadAllText(readmePath).ToLowerInvariant();
        int sections = 0;
        int found = 0;

        string[] expectedSections = [
            "install", "setup", "getting started", "quick start",
            "usage", "how to", "example",
            "build", "develop", "contribut",
            "license", "licence",
            "test", "testing"
        ];

        foreach (var section in expectedSections)
        {
            sections++;
            if (content.Contains(section)) found++;
        }

        // Also check for basic structure: heading, some content
        bool hasHeading = content.Contains('#');
        bool hasSubstantialContent = content.Length > 200;

        double sectionScore = sections > 0 ? (double)found / sections : 0;
        double structureBonus = (hasHeading ? 0.1 : 0) + (hasSubstantialContent ? 0.1 : 0);

        return Math.Min(1.0, sectionScore + structureBonus);
    }

    /// <summary>
    /// Score doc freshness: how recently were docs updated relative to code?
    /// </summary>
    internal static async Task<double> ScoreDocFreshnessAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "log -1 --format=%aI -- *.md README* CLAUDE* docs/",
                WorkingDirectory = path,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return 0.5;

            var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();

            if (string.IsNullOrEmpty(output)) return 0.3;

            if (DateTime.TryParse(output, out var lastDocUpdate))
            {
                var daysSinceUpdate = (DateTime.UtcNow - lastDocUpdate.ToUniversalTime()).TotalDays;
                if (daysSinceUpdate <= 7) return 1.0;
                if (daysSinceUpdate <= 30) return 0.8;
                if (daysSinceUpdate <= 90) return 0.6;
                if (daysSinceUpdate <= 180) return 0.4;
                return 0.2;
            }
        }
        catch { }

        return 0.5;
    }

    /// <summary>
    /// Score onboarding documentation based on presence of setup and tooling files.
    /// </summary>
    internal static double ScoreOnboardingDocs(string path)
    {
        int checks = 0;
        int passed = 0;

        checks++; if (HasFile(path, "README.md") || HasFile(path, "readme.md")) passed++;
        checks++; if (HasFile(path, ".editorconfig")) passed++;
        checks++; if (HasFile(path, "global.json")) passed++;
        checks++; if (HasFile(path, "docker-compose.yml") || HasFile(path, "docker-compose.yaml")
                      || HasFile(path, "Dockerfile")) passed++;
        checks++; if (Directory.Exists(Path.Combine(path, ".devcontainer"))) passed++;
        checks++; if (HasFile(path, "CONTRIBUTING.md") || HasFile(path, "contributing.md")) passed++;

        return (double)passed / checks;
    }

    private static bool HasFile(string root, string name) =>
        File.Exists(Path.Combine(root, name));
}
