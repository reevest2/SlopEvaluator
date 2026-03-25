using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Collects PR metrics, issue stats, and review data from GitHub via gh CLI.
/// Enhances TeamProcessMetrics with real PR data.
/// </summary>
public class GitHubMetricsCollector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Detect the GitHub remote and collect metrics.
    /// </summary>
    public async Task<GitHubMetrics?> CollectAsync(string repoPath)
    {
        var remote = await GetGitHubRemoteAsync(repoPath);
        if (remote is null) return null;

        var (owner, repo) = remote.Value;

        var mergedPrs = await GetMergedPrsAsync(owner, repo, 100);
        var openPrs = await GetOpenPrsAsync(owner, repo);
        var recentReviews = await GetReviewStatsAsync(owner, repo, mergedPrs.Take(20).ToList());

        return new GitHubMetrics
        {
            Owner = owner,
            Repo = repo,
            PullRequests = ComputePrMetrics(mergedPrs, openPrs),
            ReviewStats = recentReviews,
            TotalPrCount = mergedPrs.Count + openPrs.Count,
            OpenIssueCount = await GetOpenIssueCountAsync(owner, repo)
        };
    }

    /// <summary>
    /// Merge GitHub metrics into an existing TeamProcessMetrics.
    /// </summary>
    public static TeamProcessMetrics EnrichWithGitHub(TeamProcessMetrics existing, GitHubMetrics github)
    {
        return existing with
        {
            PrCycleTimeHealth = ScorePrCycleTime(github.PullRequests),
            ReviewQuality = ScoreReviewQuality(github.ReviewStats),
            PullRequests = github.PullRequests
        };
    }

    // --- GitHub API calls via gh CLI ---

    private static async Task<(string owner, string repo)?> GetGitHubRemoteAsync(string path)
    {
        var output = await RunGhAsync(path, "repo view --json owner,name --jq \".owner.login + \\\"/\\\" + .name\"");
        if (string.IsNullOrWhiteSpace(output)) return null;

        var parts = output.Trim().Split('/');
        return parts.Length == 2 ? (parts[0], parts[1]) : null;
    }

    private static async Task<List<PrData>> GetMergedPrsAsync(string owner, string repo, int count)
    {
        var json = await RunGhApiAsync(
            $"repos/{owner}/{repo}/pulls?state=closed&sort=updated&direction=desc&per_page={count}");
        if (string.IsNullOrEmpty(json)) return [];

        try
        {
            var prs = JsonSerializer.Deserialize<List<PrDataRaw>>(json, JsonOptions) ?? [];
            return prs
                .Where(p => p.MergedAt is not null)
                .Select(p => new PrData
                {
                    Number = p.Number,
                    Title = p.Title ?? "",
                    CreatedAt = p.CreatedAt,
                    MergedAt = p.MergedAt!.Value,
                    ReviewComments = p.ReviewComments,
                    Additions = p.Additions,
                    Deletions = p.Deletions,
                    Comments = p.Comments
                })
                .ToList();
        }
        catch { return []; }
    }

    private static async Task<List<PrData>> GetOpenPrsAsync(string owner, string repo)
    {
        var json = await RunGhApiAsync(
            $"repos/{owner}/{repo}/pulls?state=open&per_page=100");
        if (string.IsNullOrEmpty(json)) return [];

        try
        {
            var prs = JsonSerializer.Deserialize<List<PrDataRaw>>(json, JsonOptions) ?? [];
            return prs.Select(p => new PrData
            {
                Number = p.Number,
                Title = p.Title ?? "",
                CreatedAt = p.CreatedAt,
                MergedAt = DateTime.MinValue,
                ReviewComments = p.ReviewComments,
                Additions = p.Additions,
                Deletions = p.Deletions,
                Comments = p.Comments
            }).ToList();
        }
        catch { return []; }
    }

    private static async Task<ReviewStats> GetReviewStatsAsync(string owner, string repo, List<PrData> prs)
    {
        int totalReviews = 0;
        int approvedWithoutComments = 0;
        var timeToFirstReview = new List<TimeSpan>();

        foreach (var pr in prs.Take(10)) // sample 10 for speed
        {
            var json = await RunGhApiAsync($"repos/{owner}/{repo}/pulls/{pr.Number}/reviews");
            if (string.IsNullOrEmpty(json)) continue;

            try
            {
                var reviews = JsonSerializer.Deserialize<List<ReviewDataRaw>>(json, JsonOptions) ?? [];
                totalReviews += reviews.Count;

                if (reviews.Count > 0)
                {
                    var firstReview = reviews.OrderBy(r => r.SubmittedAt).First();
                    timeToFirstReview.Add(firstReview.SubmittedAt - pr.CreatedAt);

                    bool hasApprovalOnly = reviews.Any(r => r.State == "APPROVED")
                        && !reviews.Any(r => r.Body?.Length > 10);
                    if (hasApprovalOnly) approvedWithoutComments++;
                }
            }
            catch { }
        }

        return new ReviewStats
        {
            TotalReviewsSampled = totalReviews,
            MedianTimeToFirstReview = timeToFirstReview.Count > 0
                ? TimeSpan.FromTicks((long)timeToFirstReview.Select(t => t.Ticks).OrderBy(t => t).ElementAt(timeToFirstReview.Count / 2))
                : TimeSpan.Zero,
            ApprovalWithoutCommentsRate = prs.Count > 0
                ? (double)approvedWithoutComments / Math.Min(prs.Count, 10)
                : 0,
            AverageReviewsPerPr = prs.Count > 0 ? (double)totalReviews / Math.Min(prs.Count, 10) : 0
        };
    }

    private static async Task<int> GetOpenIssueCountAsync(string owner, string repo)
    {
        var json = await RunGhApiAsync($"repos/{owner}/{repo}");
        if (string.IsNullOrEmpty(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("open_issues_count").GetInt32();
        }
        catch { return 0; }
    }

    // --- Scoring ---

    private static PullRequestMetrics ComputePrMetrics(List<PrData> merged, List<PrData> open)
    {
        var mergeTimesHours = merged
            .Where(p => p.MergedAt > p.CreatedAt)
            .Select(p => (p.MergedAt - p.CreatedAt).TotalHours)
            .OrderBy(h => h)
            .ToList();

        double medianMergeHours = mergeTimesHours.Count > 0
            ? mergeTimesHours[mergeTimesHours.Count / 2]
            : 0;

        var prSizes = merged.Select(p => (double)(p.Additions + p.Deletions)).ToList();
        double avgSize = prSizes.Count > 0 ? prSizes.Average() : 0;
        double largePrRate = prSizes.Count > 0
            ? (double)prSizes.Count(s => s > 400) / prSizes.Count
            : 0;

        // PRs per week (from last 30 merged, estimate)
        int prsPerWeek = 0;
        if (merged.Count >= 2)
        {
            var span = merged.First().MergedAt - merged.Last().MergedAt;
            if (span.TotalDays > 0)
                prsPerWeek = (int)(merged.Count / (span.TotalDays / 7.0));
        }

        int stalePrs = open.Count(p => (DateTime.UtcNow - p.CreatedAt).TotalDays > 7);

        return new PullRequestMetrics
        {
            MedianTimeToFirstReview = TimeSpan.Zero, // filled by review stats
            MedianTimeToMerge = TimeSpan.FromHours(medianMergeHours),
            AverageReviewComments = merged.Count > 0
                ? merged.Average(p => p.ReviewComments + p.Comments) : 0,
            ApprovalWithoutCommentsRate = 0, // filled by review stats
            PrsPerWeek = prsPerWeek,
            AveragePrSizeLines = avgSize,
            LargePrRate = largePrRate,
            StalePrCount = stalePrs
        };
    }

    /// <summary>
    /// Score PR cycle time health based on merge speed, PR size, and stale PR count.
    /// </summary>
    public static double ScorePrCycleTime(PullRequestMetrics pr)
    {
        // Median merge time: <4h = 1.0, <24h = 0.7, <72h = 0.4, >72h = 0.2
        double mergeScore = pr.MedianTimeToMerge.TotalHours switch
        {
            <= 4 => 1.0,
            <= 24 => 0.7,
            <= 72 => 0.4,
            _ => 0.2
        };

        // PR size: <200 lines avg = 1.0, >400 = 0.3
        double sizeScore = pr.AveragePrSizeLines switch
        {
            <= 200 => 1.0,
            <= 400 => 0.7,
            <= 800 => 0.4,
            _ => 0.2
        };

        // Stale PRs: 0 = 1.0, penalty per stale
        double staleScore = pr.StalePrCount switch
        {
            0 => 1.0,
            <= 3 => 0.7,
            <= 10 => 0.4,
            _ => 0.2
        };

        return Models.ScoreAggregator.WeightedAverage(
            (mergeScore, 0.40),
            (sizeScore, 0.30),
            (staleScore, 0.30)
        );
    }

    /// <summary>
    /// Score code review quality based on coverage, rubber-stamp rate, and review speed.
    /// </summary>
    public static double ScoreReviewQuality(ReviewStats stats)
    {
        // Reviews per PR: 1+ is good, 0 is bad
        double coverageScore = stats.AverageReviewsPerPr >= 1 ? 1.0
            : stats.AverageReviewsPerPr >= 0.5 ? 0.6
            : 0.2;

        // Rubber stamp rate (approval without comments): lower is better
        double rubberStampScore = 1.0 - stats.ApprovalWithoutCommentsRate;

        // Time to first review: <2h = 1.0, <8h = 0.7, <24h = 0.4
        double speedScore = stats.MedianTimeToFirstReview.TotalHours switch
        {
            0 => 0.5, // no data
            <= 2 => 1.0,
            <= 8 => 0.7,
            <= 24 => 0.4,
            _ => 0.2
        };

        return Models.ScoreAggregator.WeightedAverage(
            (coverageScore, 0.35),
            (rubberStampScore, 0.30),
            (speedScore, 0.35)
        );
    }

    // --- gh CLI helpers ---

    private static async Task<string> RunGhAsync(string workDir, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = args,
                WorkingDirectory = workDir,
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
        catch { return ""; }
    }

    private static async Task<string> RunGhApiAsync(string endpoint)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"api {endpoint}",
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
        catch { return ""; }
    }

    // --- Data models ---

    /// <summary>
    /// Aggregated GitHub metrics including PRs, reviews, and issue counts.
    /// </summary>
    public class GitHubMetrics
    {
        /// <summary>GitHub repository owner.</summary>
        public string Owner { get; set; } = "";

        /// <summary>GitHub repository name.</summary>
        public string Repo { get; set; } = "";

        /// <summary>Pull request timing and size metrics.</summary>
        public PullRequestMetrics PullRequests { get; set; } = null!;

        /// <summary>Code review engagement and quality statistics.</summary>
        public ReviewStats ReviewStats { get; set; } = null!;

        /// <summary>Total number of pull requests in the sample.</summary>
        public int TotalPrCount { get; set; }

        /// <summary>Number of currently open issues.</summary>
        public int OpenIssueCount { get; set; }
    }

    /// <summary>
    /// Code review engagement statistics for scoring review quality.
    /// </summary>
    public class ReviewStats
    {
        /// <summary>Number of reviews sampled for analysis.</summary>
        public int TotalReviewsSampled { get; set; }

        /// <summary>Median time from PR creation to first review.</summary>
        public TimeSpan MedianTimeToFirstReview { get; set; }

        /// <summary>Rate of PRs approved without any review comments (0.0 to 1.0).</summary>
        public double ApprovalWithoutCommentsRate { get; set; }

        /// <summary>Average number of reviews per pull request.</summary>
        public double AverageReviewsPerPr { get; set; }
    }

    internal class PrData
    {
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime MergedAt { get; set; }
        public int ReviewComments { get; set; }
        public int Additions { get; set; }
        public int Deletions { get; set; }
        public int Comments { get; set; }
    }

    internal class PrDataRaw
    {
        public int Number { get; set; }
        public string? Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? MergedAt { get; set; }
        public int ReviewComments { get; set; }
        public int Additions { get; set; }
        public int Deletions { get; set; }
        public int Comments { get; set; }
    }

    internal class ReviewDataRaw
    {
        public string State { get; set; } = "";
        public string? Body { get; set; }
        public DateTime SubmittedAt { get; set; }
    }

}
