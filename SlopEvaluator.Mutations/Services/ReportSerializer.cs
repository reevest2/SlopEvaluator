using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

public static class ReportSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task WriteJsonAsync(MutationReport report, string path)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public static async Task WriteHtmlAsync(MutationReport report, string path)
    {
        var html = GenerateHtml(report);
        await File.WriteAllTextAsync(path, html);
    }

    /// <summary>
    /// Loads a config without validation — used for intermediate configs during merge/resolve.
    /// </summary>
    public static HarnessConfig LoadConfigRaw(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<HarnessConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse config from {path}");
    }

    public static HarnessConfig LoadConfig(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<HarnessConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse config from {path}");

        // Resolve includes and extends before validation
        if (config.Include is { Count: > 0 } || config.Extends is not null)
            config = ConfigMerger.Resolve(config, Path.GetDirectoryName(Path.GetFullPath(path)));

        ValidateConfig(config, path);
        return config;
    }

    /// <summary>
    /// Validates a config for common mistakes that would cause confusing runtime errors.
    /// </summary>
    public static void ValidateConfig(HarnessConfig config, string? configPath = null)
    {
        var errors = new List<string>();
        var context = configPath is not null ? $" in {configPath}" : "";

        if (string.IsNullOrWhiteSpace(config.SourceFile))
            errors.Add("sourceFile is required");

        if (string.IsNullOrWhiteSpace(config.TestCommand))
            errors.Add("testCommand is required");

        if (config.Mutations.Count == 0)
            errors.Add("At least one mutation is required");

        // Check for duplicate IDs
        var duplicateIds = config.Mutations
            .GroupBy(m => m.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
            errors.Add($"Duplicate mutation IDs: {string.Join(", ", duplicateIds)}");

        // Check each mutation has required fields
        foreach (var m in config.Mutations)
        {
            if (string.IsNullOrWhiteSpace(m.OriginalCode))
                errors.Add($"Mutation {m.Id}: originalCode is required");
            if (string.IsNullOrWhiteSpace(m.MutatedCode))
                errors.Add($"Mutation {m.Id}: mutatedCode is required");
            if (m.OriginalCode == m.MutatedCode)
                errors.Add($"Mutation {m.Id}: originalCode and mutatedCode are identical");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Config validation failed{context}:\n  - {string.Join("\n  - ", errors)}");
    }

    internal static string GenerateHtml(MutationReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>Mutation Report \u2014 {Escape(report.Target)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0f1117;
                   color: #e1e4e8; padding: 2rem; line-height: 1.6; }
            .container { max-width: 900px; margin: 0 auto; }
            h1 { font-size: 1.5rem; color: #58a6ff; margin-bottom: 0.25rem; }
            .subtitle { color: #8b949e; margin-bottom: 2rem; }
            .score-card { display: flex; gap: 1rem; margin-bottom: 2rem; flex-wrap: wrap; }
            .score-box { background: #161b22; border: 1px solid #30363d; border-radius: 8px;
                         padding: 1.25rem; flex: 1; min-width: 120px; text-align: center; }
            .score-box .value { font-size: 2rem; font-weight: 700; }
            .score-box .label { font-size: 0.8rem; color: #8b949e; text-transform: uppercase;
                                letter-spacing: 0.05em; }
            .killed .value { color: #3fb950; }
            .survived .value { color: #f85149; }
            .compile .value { color: #d29922; }
            .score .value { color: #58a6ff; }
            .section { margin-bottom: 2rem; }
            .section h2 { font-size: 1.1rem; color: #c9d1d9; border-bottom: 1px solid #30363d;
                          padding-bottom: 0.5rem; margin-bottom: 1rem; }
            .mutant-card { background: #161b22; border: 1px solid #30363d; border-radius: 6px;
                           padding: 1rem; margin-bottom: 0.75rem; }
            .mutant-header { display: flex; justify-content: space-between; align-items: center;
                             margin-bottom: 0.5rem; }
            .mutant-id { font-weight: 600; font-family: monospace; }
            .badge { padding: 2px 8px; border-radius: 12px; font-size: 0.75rem;
                     font-weight: 600; }
            .badge-killed { background: #1b3d2f; color: #3fb950; }
            .badge-survived { background: #3d1b1b; color: #f85149; }
            .badge-compile { background: #3d2e1b; color: #d29922; }
            .badge-timeout { background: #1b2a3d; color: #58a6ff; }
            .badge-high { background: #3d1b1b; color: #f85149; }
            .badge-medium { background: #3d2e1b; color: #d29922; }
            .badge-low { background: #1b3d2f; color: #3fb950; }
            .mutant-detail { font-size: 0.9rem; color: #8b949e; }
            .mutant-detail strong { color: #c9d1d9; }
            .rec-section { background: #161b22; border: 1px solid #30363d; border-radius: 8px;
                           padding: 1.25rem; }
            .rec-item { padding: 0.5rem 0; border-bottom: 1px solid #21262d; }
            .rec-item:last-child { border-bottom: none; }
            .meta { font-size: 0.8rem; color: #6e7681; margin-top: 1.5rem; }
        """);
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<div class=\"container\">");

        // Header
        sb.AppendLine($"<h1>Mutation Report \u2014 {Escape(report.Target)}</h1>");
        sb.AppendLine($"<div class=\"subtitle\">{Escape(report.SourceFile)} \u00b7 " +
                       $"{report.RunDate:yyyy-MM-dd HH:mm} UTC \u00b7 {report.TotalDuration.TotalSeconds:F1}s</div>");

        // Score cards
        sb.AppendLine("<div class=\"score-card\">");
        sb.AppendLine($"""
            <div class="score-box score"><div class="value">{report.MutationScore:F0}%</div>
            <div class="label">Mutation Score</div></div>
            <div class="score-box killed"><div class="value">{report.Killed}</div>
            <div class="label">Killed</div></div>
            <div class="score-box survived"><div class="value">{report.Survived}</div>
            <div class="label">Survived</div></div>
            <div class="score-box compile"><div class="value">{report.CompileErrors}</div>
            <div class="label">Compile Errors</div></div>
        """);
        sb.AppendLine("</div>");

        // Survivors section
        var survivors = report.Results
            .Where(r => r.Outcome == MutationOutcome.Survived)
            .OrderByDescending(r => r.RiskLevel == "high" ? 3 : r.RiskLevel == "medium" ? 2 : 1)
            .ToList();

        if (survivors.Count > 0)
        {
            sb.AppendLine("<div class=\"section\"><h2>Surviving Mutants \u2014 Test Gaps</h2>");
            foreach (var s in survivors)
            {
                sb.AppendLine("<div class=\"mutant-card\">");
                sb.AppendLine($"<div class=\"mutant-header\">");
                sb.AppendLine($"  <span class=\"mutant-id\">{Escape(s.Id)}</span>");
                sb.AppendLine($"  <span><span class=\"badge badge-survived\">SURVIVED</span> " +
                              $"<span class=\"badge badge-{s.RiskLevel}\">{Escape(s.RiskLevel.ToUpper())}</span></span>");
                sb.AppendLine("</div>");
                sb.AppendLine($"<div class=\"mutant-detail\"><strong>Strategy:</strong> {Escape(s.Strategy)}</div>");
                sb.AppendLine($"<div class=\"mutant-detail\"><strong>Description:</strong> {Escape(s.Description)}</div>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }

        // All results section
        sb.AppendLine("<div class=\"section\"><h2>All Mutations</h2>");
        foreach (var r in report.Results)
        {
            var badgeClass = r.Outcome switch
            {
                MutationOutcome.Killed => "badge-killed",
                MutationOutcome.Survived => "badge-survived",
                MutationOutcome.CompileError => "badge-compile",
                MutationOutcome.Timeout => "badge-timeout",
                _ => "badge-compile"
            };
            sb.AppendLine("<div class=\"mutant-card\">");
            sb.AppendLine($"<div class=\"mutant-header\">");
            sb.AppendLine($"  <span class=\"mutant-id\">{Escape(r.Id)}</span>");
            sb.AppendLine($"  <span class=\"badge {badgeClass}\">{r.Outcome.ToString().ToUpper()}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<div class=\"mutant-detail\">{Escape(r.Description)} \u00b7 {r.Duration.TotalSeconds:F1}s</div>");
            if (r.OriginalCode is not null && r.MutatedCode is not null)
            {
                var line = r.LineNumberHint.HasValue ? $" <span style=\"color:#6e7681\">(line {r.LineNumberHint})</span>" : "";
                sb.AppendLine($"<div class=\"mutant-detail\" style=\"margin-top:0.4rem\">{line}</div>");
                sb.AppendLine($"<pre style=\"background:#1a1e24;padding:0.5rem;border-radius:4px;font-size:0.82rem;overflow-x:auto;margin-top:0.25rem\">");
                sb.AppendLine($"<span style=\"color:#f85149\">\u2212 {Escape(r.OriginalCode)}</span>");
                sb.AppendLine($"<span style=\"color:#3fb950\">+ {Escape(r.MutatedCode)}</span>");
                sb.AppendLine("</pre>");
            }
            if (r.FailedTestNames is not null)
                sb.AppendLine($"<div class=\"mutant-detail\"><strong>Failed:</strong> {Escape(r.FailedTestNames)}</div>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");

        // Survivor analysis recommendations
        if (report.Recommendations is { Count: > 0 })
        {
            sb.AppendLine("<div class=\"section\"><h2>Survivor Analysis \u2014 Recommendations</h2>");
            foreach (var recommendation in report.Recommendations)
            {
                var priorityColor = recommendation.Priority switch
                {
                    "high" => "#f85149",
                    "medium" => "#d29922",
                    _ => "#3fb950"
                };
                sb.AppendLine($"<div class=\"mutant-card\" style=\"border-left:3px solid {priorityColor}\">");
                sb.AppendLine($"<div class=\"mutant-header\">");
                sb.AppendLine($"  <span class=\"badge\" style=\"background:{priorityColor};color:#fff\">{recommendation.Priority.ToUpperInvariant()}</span>");
                sb.AppendLine($"  <strong>{Escape(recommendation.Title)}</strong>");
                sb.AppendLine("</div>");
                sb.AppendLine($"<div class=\"mutant-detail\">{Escape(recommendation.Description)}</div>");
                sb.AppendLine($"<div class=\"mutant-detail\" style=\"margin-top:0.5rem\"><strong>Fix:</strong> {Escape(recommendation.SuggestedTestPattern)}</div>");

                if (recommendation.BoundaryTests is { Count: > 0 })
                {
                    sb.AppendLine("<div style=\"margin-top:0.5rem;padding:0.5rem;background:#1a1e24;border-radius:4px;font-size:0.85rem\">");
                    foreach (var bt in recommendation.BoundaryTests)
                    {
                        sb.AppendLine($"<div style=\"margin-bottom:0.25rem\">" +
                            $"<span style=\"color:#6e7681\">Line {bt.LineNumber}:</span> " +
                            $"<code>{Escape(bt.OriginalOperator)}</code> \u2192 <code>{Escape(bt.MutatedOperator)}</code> " +
                            $"threshold=<strong>{Escape(bt.ThresholdExpression)}</strong> \u2192 " +
                            $"test with {Escape(string.Join(", ", bt.TestValues))}</div>");
                    }
                    sb.AppendLine("</div>");
                }

                if (recommendation.AffectedLines is { Count: > 0 })
                    sb.AppendLine($"<div class=\"mutant-detail\" style=\"color:#6e7681\">Lines: {string.Join(", ", recommendation.AffectedLines)}</div>");

                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }

        // Recommended test validation
        if (report.RecommendedTestValidation is { PassesOnOriginal: true } rec)
        {
            sb.AppendLine("<div class=\"section\"><h2>AI-Recommended Test Validation</h2>");
            sb.AppendLine("<div class=\"rec-section\">");
            sb.AppendLine($"<div class=\"mutant-detail\" style=\"margin-bottom:0.75rem\">" +
                          $"<strong>{rec.MutantsNowKilled}</strong> mutants now killed \u00b7 " +
                          $"<strong>{rec.MutantsStillSurviving}</strong> still surviving</div>");
            foreach (var m in rec.MutantResults)
            {
                var icon = m.NowKilled ? "\U0001f5e1\ufe0f" : "\U0001f6e1\ufe0f";
                sb.AppendLine($"<div class=\"rec-item\">{icon} <strong>{Escape(m.MutationId)}</strong>: " +
                              $"{Escape(m.Details ?? "")}</div>");
            }
            sb.AppendLine("</div></div>");
        }

        sb.AppendLine($"<div class=\"meta\">Baseline: {report.BaselineTestCount} tests in " +
                       $"{report.BaselineDuration.TotalSeconds:F1}s</div>");
        sb.AppendLine("</div></body></html>");

        return sb.ToString();
    }

    private static string Escape(string text) =>
        System.Net.WebUtility.HtmlEncode(text);
}
