using System.Diagnostics;
using System.Text.RegularExpressions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Analyzes git history for team process metrics: commit hygiene,
/// knowledge distribution, bus factor.
/// </summary>
public class TeamProcessCollector
{
    /// <summary>
    /// Analyze git history to produce team process metrics for a repository.
    /// </summary>
    public async Task<TeamProcessMetrics> CollectAsync(string repoPath)
    {
        var commits = await GetCommitMetricsAsync(repoPath);
        var knowledge = await GetKnowledgeMetricsAsync(repoPath);
        var prMetrics = GetDefaultPrMetrics(); // would need GitHub API for real data

        double commitHygiene = ScoreCommitHygiene(commits);
        double knowledgeDist = ScoreKnowledgeDistribution(knowledge);
        double branchStrategy = await ScoreBranchStrategyAsync(repoPath);

        return new TeamProcessMetrics
        {
            PrCycleTimeHealth = 0.5,          // needs GitHub/Azure DevOps API
            ReviewQuality = 0.5,               // needs PR comment analysis
            KnowledgeDistribution = knowledgeDist,
            CommitHygiene = commitHygiene,
            BranchStrategy = branchStrategy,
            IncidentResponseHealth = 0.5,      // needs incident tracking data

            PullRequests = prMetrics,
            Commits = commits,
            Knowledge = knowledge
        };
    }

    internal static async Task<CommitMetrics> GetCommitMetricsAsync(string path)
    {
        // Get commit stats for last 30 days
        var logOutput = await RunGitAsync(path,
            "log --oneline --since='30 days ago' --format='%H|%s|%an'");
        var lines = logOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        int totalCommits = lines.Length;
        int conventionalCommits = 0;
        int mergeCommits = 0;

        foreach (var line in lines)
        {
            var parts = line.Split('|', 3);
            if (parts.Length < 2) continue;
            var message = parts.Length >= 2 ? parts[1] : "";

            if (IsConventionalCommit(message))
                conventionalCommits++;
            if (message.StartsWith("Merge ", StringComparison.OrdinalIgnoreCase))
                mergeCommits++;
        }

        // Get average commit size (insertions + deletions)
        var statOutput = await RunGitAsync(path,
            "log --since='30 days ago' --shortstat --format=''");
        var statLines = statOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        double totalChanges = 0;
        int statCount = 0;

        foreach (var line in statLines)
        {
            var insertions = Regex.Match(line, @"(\d+) insertion");
            var deletions = Regex.Match(line, @"(\d+) deletion");
            int ins = insertions.Success ? int.Parse(insertions.Groups[1].Value) : 0;
            int del = deletions.Success ? int.Parse(deletions.Groups[1].Value) : 0;
            totalChanges += ins + del;
            statCount++;
        }

        // Force pushes
        var reflogOutput = await RunGitAsync(path, "reflog --since='30 days ago' --format='%gs'");
        int forcePushes = reflogOutput.Split('\n')
            .Count(l => l.Contains("force", StringComparison.OrdinalIgnoreCase));

        double daysInRange = 30.0;
        return new CommitMetrics
        {
            AverageCommitSize = statCount > 0 ? totalChanges / statCount : 0,
            ConventionalCommitCompliance = totalCommits > 0
                ? (double)conventionalCommits / totalCommits : 0,
            CommitsPerDayAverage = totalCommits / daysInRange,
            MergeCommitRate = totalCommits > 0 ? (double)mergeCommits / totalCommits : 0,
            ForcePushCount = forcePushes
        };
    }

    internal static async Task<KnowledgeMetrics> GetKnowledgeMetricsAsync(string path)
    {
        // Get commits by author
        var shortlogOutput = await RunGitAsync(path, "shortlog -sn --all --no-merges");
        var authorCommits = new Dictionary<string, int>();

        foreach (var line in shortlogOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line.Trim(), @"^\s*(\d+)\s+(.+)$");
            if (match.Success)
            {
                var count = int.Parse(match.Groups[1].Value);
                var author = match.Groups[2].Value.Trim();
                authorCommits[author] = count;
            }
        }

        int totalCommits = authorCommits.Values.Sum();
        double topContributorConcentration = totalCommits > 0 && authorCommits.Count > 0
            ? (double)authorCommits.Values.Max() / totalCommits
            : 1.0;

        // Active contributors in last 30 days
        var recentOutput = await RunGitAsync(path,
            "shortlog -sn --since='30 days ago' --no-merges");
        int activeContributors = recentOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // Bus factor: how many authors needed to cover 50% of commits
        int busFactor = ComputeBusFactor(authorCommits);

        // Files with single owner
        var lsOutput = await RunGitAsync(path, "ls-files -- '*.cs'");
        var files = lsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int singleOwner = 0;
        int sampledFiles = 0;

        // Sample up to 100 files for ownership analysis (full scan too slow)
        var fileSample = files.Length > 100
            ? files.OrderBy(_ => Guid.NewGuid()).Take(100).ToArray()
            : files;

        foreach (var file in fileSample)
        {
            var blameOutput = await RunGitAsync(path, $"shortlog -sn -- \"{file}\"");
            var authors = blameOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            sampledFiles++;
            if (authors <= 1) singleOwner++;
        }

        double singleOwnerPercent = sampledFiles > 0 ? (double)singleOwner / sampledFiles : 0;

        return new KnowledgeMetrics
        {
            BusFactor = busFactor,
            TopContributorConcentration = topContributorConcentration,
            ActiveContributors30d = activeContributors,
            CommitsByAuthor = authorCommits,
            FileOwnership = new Dictionary<string, List<string>>(), // expensive — skip for now
            SingleOwnerFileCount = singleOwner,
            SingleOwnerFilePercent = singleOwnerPercent
        };
    }

    /// <summary>
    /// Bus factor: minimum authors whose commits cover 50% of total.
    /// </summary>
    internal static int ComputeBusFactor(Dictionary<string, int> authorCommits)
    {
        if (authorCommits.Count == 0) return 0;

        int total = authorCommits.Values.Sum();
        int threshold = total / 2;
        int accumulated = 0;
        int count = 0;

        foreach (var kv in authorCommits.OrderByDescending(x => x.Value))
        {
            accumulated += kv.Value;
            count++;
            if (accumulated >= threshold)
                return count;
        }

        return count;
    }

    internal static double ScoreCommitHygiene(CommitMetrics commits)
    {
        // Conventional commits: higher is better
        double conventionalScore = commits.ConventionalCommitCompliance;

        // Average commit size: sweet spot is 10-200 lines
        double sizeScore;
        if (commits.AverageCommitSize <= 200) sizeScore = 1.0;
        else if (commits.AverageCommitSize <= 500) sizeScore = 0.6;
        else sizeScore = 0.3;

        // Low merge commit rate is cleaner (rebase preferred)
        double mergeScore = 1.0 - commits.MergeCommitRate;

        // No force pushes
        double forcePushScore = commits.ForcePushCount == 0 ? 1.0
            : commits.ForcePushCount <= 2 ? 0.7
            : 0.3;

        return Models.ScoreAggregator.WeightedAverage(
            (conventionalScore, 0.25),
            (sizeScore, 0.30),
            (mergeScore, 0.20),
            (forcePushScore, 0.25)
        );
    }

    internal static double ScoreKnowledgeDistribution(KnowledgeMetrics knowledge)
    {
        // Bus factor: 1 is bad, 3+ is good
        double busScore = knowledge.BusFactor switch
        {
            >= 3 => 1.0,
            2 => 0.7,
            1 => 0.3,
            _ => 0.0
        };

        // Top contributor concentration: lower is better
        double concScore = 1.0 - knowledge.TopContributorConcentration;

        // Single owner files: lower is better
        double singleOwnerScore = 1.0 - knowledge.SingleOwnerFilePercent;

        return Models.ScoreAggregator.WeightedAverage(
            (busScore, 0.40),
            (concScore, 0.30),
            (singleOwnerScore, 0.30)
        );
    }

    internal static async Task<double> ScoreBranchStrategyAsync(string path)
    {
        // Check for protected main branch patterns
        var branchOutput = await RunGitAsync(path, "branch -a");
        var branches = branchOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        bool hasMain = branches.Any(b => b.Contains("main") || b.Contains("master"));
        bool hasDevelop = branches.Any(b => b.Contains("develop") || b.Contains("dev"));
        bool hasFeatureBranches = branches.Any(b =>
            b.Contains("feature/") || b.Contains("fix/") || b.Contains("issue/"));

        int score = 0;
        if (hasMain) score++;
        if (hasDevelop || hasFeatureBranches) score++;
        if (hasFeatureBranches) score++;

        return score / 3.0;
    }

    private static PullRequestMetrics GetDefaultPrMetrics() => new()
    {
        MedianTimeToFirstReview = TimeSpan.FromHours(4),
        MedianTimeToMerge = TimeSpan.FromHours(8),
        AverageReviewComments = 0,
        ApprovalWithoutCommentsRate = 0,
        PrsPerWeek = 0,
        AveragePrSizeLines = 0,
        LargePrRate = 0,
        StalePrCount = 0
    };

    internal static bool IsConventionalCommit(string message)
    {
        return Regex.IsMatch(message, @"^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\(.+\))?[!]?:");
    }

    private static async Task<string> RunGitAsync(string path, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
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

}
