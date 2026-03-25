using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Scans solution/project files and source layout to produce a ProjectStructure score.
/// </summary>
public class ProjectStructureCollector
{
    private readonly ILogger<ProjectStructureCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectStructureCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public ProjectStructureCollector(ILogger<ProjectStructureCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<ProjectStructureCollector>.Instance;
    }

    /// <summary>
    /// Scan a directory containing a .NET solution.
    /// </summary>
    public async Task<ProjectStructure> CollectAsync(string solutionPath)
    {
        _logger.LogDebug("Starting architecture analysis for {SolutionPath}", solutionPath);
        var projects = await DiscoverProjectsAsync(solutionPath);
        int totalFiles = 0;
        int totalLines = 0;

        foreach (var proj in projects)
        {
            var csFiles = GetSourceFiles(Path.GetDirectoryName(proj.CsprojPath)!);
            proj.SourceFileCount = csFiles.Count;
            int lines = 0;
            foreach (var f in csFiles)
                lines += (await File.ReadAllLinesAsync(f)).Length;
            proj.TotalLines = lines;
            totalFiles += csFiles.Count;
            totalLines += lines;
        }

        double namingConsistency = ScoreNamingConsistency(projects);
        double folderOrg = ScoreFolderOrganization(solutionPath, projects);
        double hygiene = ScoreSolutionHygiene(solutionPath);
        double granularity = ScoreProjectGranularity(projects, totalFiles);

        // Detect god-class-like projects (oversized)
        var oversizedProjects = projects.Where(p => p.TotalLines > 5000).ToList();
        if (oversizedProjects.Count > 0)
            _logger.LogWarning("Detected {Count} oversized projects (>5000 lines): {Projects}", oversizedProjects.Count, string.Join(", ", oversizedProjects.Select(p => p.Name)));

        _logger.LogInformation("Architecture summary: naming={NamingScore:F3}, folderOrg={FolderScore:F3}, granularity={GranularityScore:F3} across {ProjectCount} projects",
            namingConsistency, folderOrg, granularity, projects.Count);

        return new ProjectStructure
        {
            ProjectCount = projects.Count,
            SourceFiles = totalFiles,
            TotalLines = totalLines,
            NamingConsistency = namingConsistency,
            FolderOrganization = folderOrg,
            SolutionHygiene = hygiene,
            ProjectGranularity = granularity,
            Projects = projects.Select(p => new ProjectInfo
            {
                Name = p.Name,
                SdkType = p.SdkType,
                OutputType = p.OutputType,
                SourceFileCount = p.SourceFileCount,
                TotalLines = p.TotalLines,
                ProjectReferences = p.ProjectReferences,
                PackageReferences = p.PackageReferences
            }).ToList()
        };
    }

    private async Task<List<ProjectData>> DiscoverProjectsAsync(string path)
    {
        var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Replace('\\', '/').Contains("/obj/")
                     && !f.Replace('\\', '/').Contains("/bin/")
                     && !f.Replace('\\', '/').Contains("/benchmarks/"))
            .ToList();

        var projects = new List<ProjectData>();
        foreach (var csproj in csprojFiles)
        {
            var xml = await File.ReadAllTextAsync(csproj);
            var doc = XDocument.Parse(xml);

            var sdk = doc.Root?.Attribute("Sdk")?.Value ?? "Microsoft.NET.Sdk";
            var outputType = doc.Descendants("OutputType").FirstOrDefault()?.Value ?? "Library";
            var projRefs = doc.Descendants("ProjectReference")
                .Select(e => e.Attribute("Include")?.Value ?? "")
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => Path.GetFileNameWithoutExtension(v))
                .ToList();
            var pkgRefs = doc.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value ?? "")
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            projects.Add(new ProjectData
            {
                Name = Path.GetFileNameWithoutExtension(csproj),
                CsprojPath = csproj,
                SdkType = sdk,
                OutputType = outputType,
                ProjectReferences = projRefs,
                PackageReferences = pkgRefs
            });
        }

        return projects;
    }

    /// <summary>
    /// Check naming conventions: PascalCase projects, consistent suffixes (.Tests, .Core, etc.)
    /// </summary>
    internal static double ScoreNamingConsistency<T>(List<T> projects) where T : TestableProjectData
    {
        if (projects.Count == 0) return 1.0;

        int consistent = 0;
        foreach (var p in projects)
        {
            bool pascalCase = char.IsUpper(p.Name[0]);
            bool noSpaces = !p.Name.Contains(' ');
            bool noSpecialChars = p.Name.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');
            if (pascalCase && noSpaces && noSpecialChars)
                consistent++;
        }

        return (double)consistent / projects.Count;
    }

    /// <summary>
    /// Check folder organization: are projects in their own folders, proper src/test separation?
    /// </summary>
    internal static double ScoreFolderOrganization<T>(string root, List<T> projects) where T : TestableProjectData
    {
        if (projects.Count == 0) return 1.0;

        int inOwnFolder = 0;
        bool hasSrcFolder = Directory.Exists(Path.Combine(root, "src"));
        bool hasTestSeparation = projects.Any(p =>
            p.Name.Contains("Test", StringComparison.OrdinalIgnoreCase));

        foreach (var p in projects)
        {
            var projDir = Path.GetDirectoryName(p.CsprojPath)!;
            var dirName = Path.GetFileName(projDir);
            // Project should be in a folder matching its name (or close)
            if (dirName.Equals(p.Name, StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains(dirName, StringComparison.OrdinalIgnoreCase))
                inOwnFolder++;
        }

        double folderScore = (double)inOwnFolder / projects.Count;
        double structureBonus = (hasSrcFolder ? 0.1 : 0) + (hasTestSeparation ? 0.1 : 0);

        return Math.Min(1.0, folderScore + structureBonus);
    }

    /// <summary>
    /// Solution hygiene: .gitignore, .editorconfig, solution file exists, no orphans.
    /// </summary>
    internal static double ScoreSolutionHygiene(string root)
    {
        int checks = 0;
        int passed = 0;

        checks++; if (HasFile(root, ".gitignore")) passed++;
        checks++; if (HasFile(root, ".editorconfig")) passed++;
        checks++; if (HasSolutionFile(root)) passed++;
        checks++; if (HasFile(root, "global.json")) passed++;
        checks++; if (!HasFile(root, ".vs")) passed++; // .vs folder shouldn't be committed

        return (double)passed / checks;
    }

    /// <summary>
    /// Project granularity: are there an appropriate number of projects for the codebase size?
    /// Too few = monolith blob. Too many = over-engineered. Sweet spot depends on total files.
    /// </summary>
    internal static double ScoreProjectGranularity<T>(List<T> projects, int totalFiles) where T : TestableProjectData
    {
        if (projects.Count == 0 || totalFiles == 0) return 0.5;

        double filesPerProject = (double)totalFiles / projects.Count;

        // Sweet spot: 20-150 files per project
        if (filesPerProject >= 20 && filesPerProject <= 150)
            return 1.0;
        if (filesPerProject < 5) // too many tiny projects
            return 0.4;
        if (filesPerProject > 500) // too few projects for file count
            return 0.4;

        // Linear scale outside sweet spot
        if (filesPerProject < 20)
            return 0.4 + 0.6 * (filesPerProject - 5) / 15.0;
        // > 150
        return Math.Max(0.4, 1.0 - (filesPerProject - 150) / 350.0);
    }

    private static bool HasFile(string root, string name) =>
        File.Exists(Path.Combine(root, name)) || Directory.Exists(Path.Combine(root, name));

    private static bool HasSolutionFile(string root) =>
        Directory.GetFiles(root, "*.sln").Length > 0
        || Directory.GetFiles(root, "*.slnx").Length > 0;

    private static List<string> GetSourceFiles(string projectDir)
    {
        if (!Directory.Exists(projectDir)) return [];
        return Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/")
                     && !f.Replace('\\', '/').Contains("/bin/"))
            .ToList();
    }

    /// <summary>
    /// Exposed for testing scoring methods.
    /// </summary>
    public class TestableProjectData
    {
        /// <summary>Project name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Path to the .csproj file.</summary>
        public string CsprojPath { get; set; } = "";

        /// <summary>SDK type (e.g. "Microsoft.NET.Sdk").</summary>
        public string SdkType { get; set; } = "";

        /// <summary>Output type (e.g. "Library", "Exe").</summary>
        public string OutputType { get; set; } = "";

        /// <summary>Number of source files in the project.</summary>
        public int SourceFileCount { get; set; }

        /// <summary>Total lines of code in the project.</summary>
        public int TotalLines { get; set; }

        /// <summary>Other projects referenced by this project.</summary>
        public List<string> ProjectReferences { get; set; } = [];

        /// <summary>NuGet packages referenced by this project.</summary>
        public List<string> PackageReferences { get; set; } = [];
    }

    private class ProjectData : TestableProjectData { }
}
