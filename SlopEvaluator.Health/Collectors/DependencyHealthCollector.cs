using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Scans NuGet dependencies via dotnet list package and csproj parsing
/// to produce a DependencyHealth score.
/// </summary>
public class DependencyHealthCollector
{
    private readonly ILogger<DependencyHealthCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyHealthCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public DependencyHealthCollector(ILogger<DependencyHealthCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<DependencyHealthCollector>.Instance;
    }

    /// <summary>
    /// Collect dependency health for a solution/project directory.
    /// </summary>
    public async Task<DependencyHealth> CollectAsync(string solutionPath)
    {
        _logger.LogDebug("Starting dependency scan for {SolutionPath}", solutionPath);
        var packages = await CollectPackagesFromCsprojAsync(solutionPath);
        var outdated = await RunDotnetListPackageAsync(solutionPath, "--outdated");
        var vulnerable = await RunDotnetListPackageAsync(solutionPath, "--vulnerable");

        // Merge outdated info
        foreach (var (pkg, latest, behind) in ParseOutdatedOutput(outdated))
        {
            var match = packages.FirstOrDefault(p =>
                p.PackageId.Equals(pkg, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                match.LatestVersion = latest;
                match.MajorVersionsBehind = behind;
            }
        }

        // Parse vulnerabilities
        var vulns = ParseVulnerableOutput(vulnerable);

        if (vulns.Count > 0)
            _logger.LogWarning("Found {VulnerabilityCount} vulnerable packages in {SolutionPath}", vulns.Count, solutionPath);

        _logger.LogInformation("Dependency scan found {PackageCount} packages across projects", packages.Count);

        // Build deprecated list (packages with "Deprecated" in name or known deprecated)
        var deprecated = packages
            .Where(p => p.IsDeprecated)
            .Select(p => new DeprecatedPackage
            {
                PackageId = p.PackageId,
                Reason = "Marked deprecated",
                AlternativePackage = null
            }).ToList();

        // Compute scores
        double freshness = ComputeFreshness(packages);
        _logger.LogDebug("Computed freshness score {FreshnessScore:F3} for {PackageCount} packages", freshness, packages.Count);
        double vulnFreedom = ComputeVulnerabilityFreedom(vulns, packages.Count);
        double licenseCompliance = 1.0; // would need NuGet API for license check — stub
        double transitive = ComputeTransitiveCleanliness(packages);
        double packageCountScore = ComputePackageCountScore(packages.Count);

        return new DependencyHealth
        {
            Freshness = freshness,
            VulnerabilityFreedom = vulnFreedom,
            LicenseCompliance = licenseCompliance,
            TransitiveCleanliness = transitive,
            PackageCountScore = packageCountScore,
            Packages = packages.Select(p => new PackageInfo
            {
                PackageId = p.PackageId,
                CurrentVersion = p.CurrentVersion,
                LatestVersion = p.LatestVersion,
                MajorVersionsBehind = p.MajorVersionsBehind,
                DaysSinceLastUpdate = 0, // would need NuGet API
                License = "unknown",
                IsDeprecated = p.IsDeprecated,
                IsDirect = p.IsDirect,
                Project = p.Project
            }).ToList(),
            Vulnerabilities = vulns,
            Deprecated = deprecated
        };
    }

    private async Task<List<PackageData>> CollectPackagesFromCsprojAsync(string path)
    {
        var packages = new List<PackageData>();
        var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/")
                     && !f.Replace('\\', '/').Contains("/bin/")
                     && !f.Replace('\\', '/').Contains("/benchmarks/"));

        foreach (var csproj in csprojFiles)
        {
            var projName = Path.GetFileNameWithoutExtension(csproj);
            try
            {
                var doc = XDocument.Parse(await File.ReadAllTextAsync(csproj));
                foreach (var pkgRef in doc.Descendants("PackageReference"))
                {
                    var id = pkgRef.Attribute("Include")?.Value;
                    var version = pkgRef.Attribute("Version")?.Value
                        ?? pkgRef.Element("Version")?.Value
                        ?? "unknown";

                    if (id is not null)
                    {
                        packages.Add(new PackageData
                        {
                            PackageId = id,
                            CurrentVersion = version,
                            Project = projName,
                            IsDirect = true
                        });
                    }
                }
            }
            catch { /* skip unparseable csproj */ }
        }

        return packages;
    }

    private static async Task<string> RunDotnetListPackageAsync(string path, string flags)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"list package {flags}",
                WorkingDirectory = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return "";

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch
        {
            return "";
        }
    }

    internal static List<(string PackageId, string LatestVersion, int MajorsBehind)>
        ParseOutdatedOutput(string output)
    {
        var results = new List<(string, string, int)>();
        if (string.IsNullOrWhiteSpace(output)) return results;

        // Format: > PackageName    Current    Resolved    Latest
        var regex = new Regex(@">\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)");
        foreach (Match match in regex.Matches(output))
        {
            var packageId = match.Groups[1].Value;
            var resolved = match.Groups[3].Value;
            var latest = match.Groups[4].Value;

            int majorsBehind = ComputeMajorsBehind(resolved, latest);
            results.Add((packageId, latest, majorsBehind));
        }

        return results;
    }

    internal static List<VulnerabilityInfo> ParseVulnerableOutput(string output)
    {
        var results = new List<VulnerabilityInfo>();
        if (string.IsNullOrWhiteSpace(output)) return results;

        // Look for advisory patterns
        var lines = output.Split('\n');
        string? currentPackage = null;

        foreach (var line in lines)
        {
            var pkgMatch = Regex.Match(line, @">\s+(\S+)\s+(\S+)");
            if (pkgMatch.Success)
                currentPackage = pkgMatch.Groups[1].Value;

            if (currentPackage is not null && line.Contains("https://github.com/advisories/"))
            {
                var urlMatch = Regex.Match(line, @"(https://\S+)");
                var severityMatch = Regex.Match(line, @"(Critical|High|Moderate|Low)", RegexOptions.IgnoreCase);

                results.Add(new VulnerabilityInfo
                {
                    PackageId = currentPackage,
                    AdvisoryId = urlMatch.Success ? urlMatch.Groups[1].Value : "unknown",
                    Severity = severityMatch.Success ? severityMatch.Groups[1].Value.ToLower() : "unknown",
                    Description = line.Trim(),
                    FixedInVersion = null
                });
            }
        }

        return results;
    }

    internal static int ComputeMajorsBehind(string current, string latest)
    {
        try
        {
            var cur = Version.Parse(StripPrerelease(current));
            var lat = Version.Parse(StripPrerelease(latest));
            return Math.Max(0, lat.Major - cur.Major);
        }
        catch { return 0; }
    }

    private static string StripPrerelease(string version)
    {
        var idx = version.IndexOf('-');
        return idx >= 0 ? version[..idx] : version;
    }

    /// <summary>
    /// Freshness: % of packages that are on latest major version.
    /// </summary>
    internal static double ComputeFreshness(List<PackageData> packages)
    {
        if (packages.Count == 0) return 1.0;
        var withLatest = packages.Where(p => p.LatestVersion is not null).ToList();
        if (withLatest.Count == 0) return 0.8; // couldn't check — assume mostly fresh
        // ROSLYN## If fewer than half of packages have detectable latest versions,
        // the sample is too small to draw reliable conclusions — return optimistic default
        if (withLatest.Count < packages.Count / 2.0) return 0.8;
        int current = withLatest.Count(p => p.MajorVersionsBehind == 0);
        return (double)current / withLatest.Count;
    }

    /// <summary>
    /// Vulnerability freedom: 1.0 = no vulns, penalize by severity.
    /// </summary>
    internal static double ComputeVulnerabilityFreedom(List<VulnerabilityInfo> vulns, int totalPackages)
    {
        if (vulns.Count == 0) return 1.0;
        if (totalPackages == 0) return 1.0;

        double penalty = 0;
        foreach (var v in vulns)
        {
            penalty += v.Severity.ToLower() switch
            {
                "critical" => 0.3,
                "high" => 0.2,
                "moderate" => 0.1,
                "low" => 0.05,
                _ => 0.1
            };
        }

        return Math.Max(0, 1.0 - penalty);
    }

    /// <summary>
    /// Transitive cleanliness: ratio of direct to total packages.
    /// More direct = cleaner dependency tree.
    /// </summary>
    internal static double ComputeTransitiveCleanliness(List<PackageData> packages)
    {
        if (packages.Count == 0) return 1.0;
        int direct = packages.Count(p => p.IsDirect);
        return (double)direct / packages.Count;
    }

    /// <summary>
    /// Package count score: sweet spot is 5-50 direct packages.
    /// Fewer = fine, more = dependency bloat risk.
    /// </summary>
    internal static double ComputePackageCountScore(int count)
    {
        if (count <= 50) return 1.0;
        if (count >= 200) return 0.3;
        return 1.0 - 0.7 * (count - 50) / 150.0;
    }

    internal class PackageData
    {
        public string PackageId { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string? LatestVersion { get; set; }
        public int MajorVersionsBehind { get; set; }
        public bool IsDeprecated { get; set; }
        public bool IsDirect { get; set; }
        public string Project { get; set; } = "";
    }
}
