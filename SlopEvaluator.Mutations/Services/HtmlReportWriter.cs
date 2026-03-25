using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Services;

/// <summary>
/// Generates the HTML quality report (dark-themed dashboard).
/// Extracted from Program.WriteQualityHtml to keep Program.cs focused on dispatch.
/// </summary>
public static class HtmlReportWriter
{
    public static async Task WriteQualityHtmlAsync(QualityReport report, string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine($"<title>Quality Report \u2014 {Esc(report.Target ?? report.SourceFile)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0f1117;
                   color: #e1e4e8; padding: 2rem; line-height: 1.6; }
            .container { max-width: 960px; margin: 0 auto; }
            h1 { font-size: 1.5rem; color: #58a6ff; margin-bottom: 0.25rem; }
            h2 { font-size: 1.15rem; color: #c9d1d9; border-bottom: 1px solid #30363d;
                 padding-bottom: 0.5rem; margin: 1.5rem 0 1rem; }
            .sub { color: #8b949e; margin-bottom: 2rem; }
            .scores { display: flex; gap: 0.75rem; flex-wrap: wrap; margin-bottom: 2rem; }
            .sb { background: #161b22; border: 1px solid #30363d; border-radius: 8px;
                  padding: 1rem; flex: 1; min-width: 110px; text-align: center; }
            .sb .v { font-size: 1.8rem; font-weight: 700; }
            .sb .l { font-size: 0.75rem; color: #8b949e; text-transform: uppercase; }
            .g .v { color: #3fb950; } .w .v { color: #d29922; }
            .b .v { color: #f85149; } .i .v { color: #58a6ff; }
            .card { background: #161b22; border: 1px solid #30363d; border-radius: 6px;
                    padding: 0.75rem 1rem; margin-bottom: 0.5rem; font-size: 0.9rem; }
            .hd { display: flex; justify-content: space-between; margin-bottom: 0.25rem; }
            .badge { padding: 2px 8px; border-radius: 12px; font-size: 0.7rem; font-weight: 600; }
            .bc { background: #3d1b1b; color: #f85149; }
            .bh { background: #3d2e1b; color: #d29922; }
            .bm { background: #1b2a3d; color: #58a6ff; }
            .bl { background: #1b3d2f; color: #3fb950; }
            .meta { font-size: 0.8rem; color: #6e7681; margin-top: 1.5rem; }
        """);
        sb.AppendLine("</style></head><body><div class=\"container\">");

        sb.AppendLine($"<h1>Quality Report \u2014 {Esc(report.Target ?? report.SourceFile)}</h1>");
        sb.AppendLine($"<div class=\"sub\">{report.RunDate:yyyy-MM-dd HH:mm} UTC \u00b7 {report.TotalDuration.TotalSeconds:F1}s</div>");

        // Score boxes
        sb.AppendLine("<div class=\"scores\">");
        ScoreBox(sb, report.Scores.LineCoverage, "Line Cov");
        ScoreBox(sb, report.Scores.BranchCoverage, "Branch Cov");
        ScoreBox(sb, report.Scores.MutationScore, "Mutation");
        ScoreBox(sb, report.Scores.EdgeCaseCoverage, "Edge Cases");
        sb.AppendLine($"""<div class="sb i"><div class="v">{report.Scores.CompositeScore:F0}%</div><div class="l">Composite</div></div>""");
        sb.AppendLine("</div>");

        // Actions
        sb.AppendLine("<h2>Action Items</h2>");
        foreach (var a in report.Actions)
        {
            var bc = a.Priority switch { "critical" => "bc", "high" => "bh", "medium" => "bm", _ => "bl" };
            var line = a.LineNumber.HasValue ? $" (line {a.LineNumber})" : "";
            sb.AppendLine($"<div class=\"card\"><div class=\"hd\"><span>{Esc(a.Description)}{line}</span>");
            sb.AppendLine($"<span class=\"badge {bc}\">{Esc(a.Priority.ToUpper())}</span></div>");
            sb.AppendLine($"<div style=\"color:#8b949e;font-size:0.8rem\">{Esc(a.Category)}</div></div>");
        }

        // Edge cases
        if (report.EdgeCases is { EdgeCases.Count: > 0 })
        {
            var uncovered = report.EdgeCases.EdgeCases
                .Where(e => e.CoveredByExistingTests != true).Take(15).ToList();
            if (uncovered.Count > 0)
            {
                sb.AppendLine("<h2>Uncovered Edge Cases</h2>");
                foreach (var ec in uncovered)
                {
                    var bc = ec.RiskLevel switch { "high" => "bc", "medium" => "bh", _ => "bl" };
                    sb.AppendLine($"<div class=\"card\"><div class=\"hd\"><span>{Esc(ec.Id)}: {Esc(ec.Description)}</span>");
                    sb.AppendLine($"<span class=\"badge {bc}\">{Esc(ec.RiskLevel.ToUpper())}</span></div>");
                    sb.AppendLine($"<div style=\"color:#8b949e;font-size:0.8rem\">\u2192 {Esc(ec.SuggestedTestName)}</div></div>");
                }
            }
        }

        sb.AppendLine($"<div class=\"meta\">Source: {Esc(report.SourceFile)}</div>");
        sb.AppendLine("</div></body></html>");

        await File.WriteAllTextAsync(path, sb.ToString());
    }

    private static void ScoreBox(System.Text.StringBuilder sb, double val, string label)
    {
        var cls = val >= 80 ? "g" : val >= 60 ? "w" : "b";
        sb.AppendLine($"""<div class="sb {cls}"><div class="v">{val:F0}%</div><div class="l">{label}</div></div>""");
    }

    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);
}
