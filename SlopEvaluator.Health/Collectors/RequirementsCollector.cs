using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Collects requirements quality from GitHub issues via gh CLI.
/// Scores clarity, completeness, testability, atomicity, and acceptance criteria.
/// </summary>
public class RequirementsCollector
{
    /// <summary>
    /// Collect requirements quality from a repo's GitHub issues.
    /// </summary>
    public async Task<RequirementsQuality> CollectAsync(string repoPath)
    {
        List<IssueData> issues = [];

        var remote = await GetGitHubRemoteAsync(repoPath);
        if (remote is not null)
        {
            var (owner, repo) = remote.Value;
            issues = await FetchRecentIssuesAsync(owner, repo, 50);
        }

        // Merge local requirements.json with GitHub issues
        var localIssues = await TryLoadLocalRequirementsAsync(repoPath);
        if (localIssues.Count > 0)
        {
            var existingNumbers = issues.Select(i => i.Number).ToHashSet();
            issues.AddRange(localIssues.Where(l => !existingNumbers.Contains(l.Number)));
        }

        if (issues.Count == 0)
            return EmptyResult();

        var assessments = issues.Select(AssessIssue).ToList();

        return new RequirementsQuality
        {
            Clarity = assessments.Average(a => a.ClarityScore),
            Completeness = assessments.Average(a => a.CompletenessScore),
            Testability = assessments.Average(a => a.TestabilityScore),
            Atomicity = ScoreAtomicity(issues),
            AcceptanceCriteriaQuality = assessments.Average(a =>
                a.HasGivenWhenThen ? 1.0 : a.AcceptanceCriteriaCount > 0 ? 0.6 : 0.0),
            TraceabilityToCode = ScoreTraceability(issues),
            Stories = assessments
        };
    }

    private static async Task<List<IssueData>> TryLoadLocalRequirementsAsync(string repoPath)
    {
        var paths = new[]
        {
            Path.Combine(repoPath, "requirements.json"),
            Path.Combine(repoPath, "docs", "requirements.json")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var issues = JsonSerializer.Deserialize<List<IssueData>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (issues?.Count > 0) return issues;
            }
            catch { /* skip unparseable */ }
        }

        return [];
    }

    private static async Task<List<IssueData>> FetchRecentIssuesAsync(string owner, string repo, int count)
    {
        try
        {
            var json = await RunGhApiAsync(
                $"repos/{owner}/{repo}/issues?state=all&per_page={count}&sort=created&direction=desc");
            if (string.IsNullOrEmpty(json)) return [];

            var raw = JsonSerializer.Deserialize<List<IssueRaw>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            return raw
                .Where(i => i.PullRequest is null) // exclude PRs
                .Select(i => new IssueData
                {
                    Number = i.Number,
                    Title = i.Title ?? "",
                    Body = i.Body ?? "",
                    Labels = i.Labels?.Select(l => l.Name ?? "").ToList() ?? []
                })
                .ToList();
        }
        catch { return []; }
    }

    internal static StoryAssessment AssessIssue(IssueData issue)
    {
        var title = issue.Title;
        var body = issue.Body;
        var combined = $"{title}\n{body}".ToLowerInvariant();

        // Clarity: title length, body length, structure
        double clarityScore = ScoreClarity(title, body);

        // Testability: mentions tests, has verifiable criteria
        double testabilityScore = ScoreTestability(combined);

        // Completeness: has description, steps, expected behavior
        double completenessScore = ScoreCompleteness(body);

        // Acceptance criteria
        bool hasGivenWhenThen = Regex.IsMatch(combined, @"\b(given|when|then)\b.*\b(given|when|then)\b");
        int acCount = CountAcceptanceCriteria(body);

        // Ambiguous terms
        var ambiguous = FindAmbiguousTerms(combined);

        // Missing scenarios
        var missing = FindMissingScenarios(combined);

        return new StoryAssessment
        {
            StoryId = $"#{issue.Number}",
            Title = title.Length > 60 ? title[..60] + "..." : title,
            ClarityScore = clarityScore,
            TestabilityScore = testabilityScore,
            CompletenessScore = completenessScore,
            AcceptanceCriteriaCount = acCount,
            HasGivenWhenThen = hasGivenWhenThen,
            AmbiguousTerms = ambiguous,
            MissingScenarios = missing,
            EstimatedTestCount = Math.Max(1, acCount * 2)
        };
    }

    internal static double ScoreClarity(string title, string body)
    {
        double score = 0;

        // Good title: 10-80 chars, starts with type prefix
        if (title.Length >= 10 && title.Length <= 80) score += 0.2;
        if (Regex.IsMatch(title, @"^(feat|fix|bug|test|docs|refactor|chore)[\(:]"))
            score += 0.2;

        // Body present and substantial
        if (body.Length > 50) score += 0.2;
        if (body.Length > 200) score += 0.1;

        // Has structure (headers, lists, code blocks)
        if (body.Contains('#') || body.Contains("- ") || body.Contains("* "))
            score += 0.15;
        if (body.Contains("```"))
            score += 0.15;

        return Math.Min(1.0, score);
    }

    internal static double ScoreTestability(string combined)
    {
        double score = 0;

        if (combined.Contains("test")) score += 0.2;
        if (combined.Contains("expect") || combined.Contains("should") || combined.Contains("must"))
            score += 0.2;
        if (combined.Contains("assert") || combined.Contains("verify"))
            score += 0.2;
        if (Regex.IsMatch(combined, @"\b(given|when|then)\b"))
            score += 0.2;
        if (combined.Contains("[ ]") || combined.Contains("[x]")) // checklist
            score += 0.2;

        return Math.Min(1.0, score);
    }

    internal static double ScoreCompleteness(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return 0.0;

        double score = 0;
        int checks = 5;

        if (body.Length > 50) score++;                    // has description
        if (body.Contains("step") || body.Contains("1.") || body.Contains("- ")) score++; // has steps
        if (body.Contains("expect") || body.Contains("should")) score++;  // expected behavior
        if (body.Contains("current") || body.Contains("actual") || body.Contains("bug")) score++; // current state
        if (body.Contains("screenshot") || body.Contains("```") || body.Contains("error")) score++; // evidence

        return score / checks;
    }

    internal static int CountAcceptanceCriteria(string body)
    {
        // Count checkboxes, numbered items in AC section, Given/When/Then blocks
        int count = 0;
        count += Regex.Matches(body, @"\[[ x]\]").Count;
        count += Regex.Matches(body, @"(?i)\bgiven\b.*\bwhen\b.*\bthen\b").Count;
        return count;
    }

    internal static List<string> FindAmbiguousTerms(string text)
    {
        string[] vague = ["somehow", "maybe", "probably", "should work", "etc", "and so on",
            "as needed", "appropriate", "reasonable", "various", "some"];
        return vague.Where(v => text.Contains(v)).ToList();
    }

    internal static List<string> FindMissingScenarios(string text)
    {
        var missing = new List<string>();
        if (!text.Contains("error") && !text.Contains("fail") && !text.Contains("invalid"))
            missing.Add("No error/failure scenarios described");
        if (!text.Contains("edge") && !text.Contains("boundary") && !text.Contains("limit"))
            missing.Add("No edge cases mentioned");
        if (!text.Contains("permission") && !text.Contains("auth") && !text.Contains("role"))
            missing.Add("No permission/auth considerations");
        return missing;
    }

    private static double ScoreAtomicity(List<IssueData> issues)
    {
        if (issues.Count == 0) return 0.5;

        // Score based on title length and body length — shorter = more atomic
        int tooLarge = 0;
        foreach (var issue in issues)
        {
            if (issue.Body.Length > 2000 || issue.Title.Contains(" and "))
                tooLarge++;
        }

        return 1.0 - (double)tooLarge / issues.Count;
    }

    private static double ScoreTraceability(List<IssueData> issues)
    {
        if (issues.Count == 0) return 0.5;

        int withCodeRef = issues.Count(i =>
            i.Body.Contains(".cs") || i.Body.Contains("```") ||
            i.Labels.Any(l => l.Contains("test") || l.Contains("code")));

        return (double)withCodeRef / issues.Count;
    }

    private static RequirementsQuality EmptyResult() => new()
    {
        Clarity = 0.5, Completeness = 0.5, Testability = 0.5,
        Atomicity = 0.5, AcceptanceCriteriaQuality = 0.5, TraceabilityToCode = 0.5,
        Stories = []
    };

    private static async Task<(string owner, string repo)?> GetGitHubRemoteAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh", Arguments = "repo view --json owner,name --jq \".owner.login + \\\"/\\\" + .name\"",
                WorkingDirectory = path,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            var parts = output.Split('/');
            return parts.Length == 2 ? (parts[0], parts[1]) : null;
        }
        catch { return null; }
    }

    private static async Task<string> RunGhApiAsync(string endpoint)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh", Arguments = $"api {endpoint}",
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }

    /// <summary>
    /// Represents a single issue or requirement for quality assessment.
    /// </summary>
    internal class IssueData
    {
        /// <summary>Issue or story number.</summary>
        public int Number { get; set; }

        /// <summary>Issue title text.</summary>
        public string Title { get; set; } = "";

        /// <summary>Issue body/description text.</summary>
        public string Body { get; set; } = "";

        /// <summary>Labels attached to the issue.</summary>
        public List<string> Labels { get; set; } = [];
    }

    private class IssueRaw
    {
        public int Number { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public List<LabelRaw>? Labels { get; set; }
        public object? PullRequest { get; set; }
    }

    private class LabelRaw
    {
        public string? Name { get; set; }
    }
}
