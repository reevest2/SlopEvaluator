using System.Text.Json;
using System.Text.Json.Serialization;
using SlopEvaluator.Mutations.Fix;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;

namespace SlopEvaluator.Mutations.Fix;

public static class ReportReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static List<Survivor> ReadSurvivors(string reportPath, string? onlyIds = null)
    {
        if (!File.Exists(reportPath))
            throw new FileNotFoundException($"Report not found: {reportPath}");

        var json = File.ReadAllText(reportPath);
        var report = JsonSerializer.Deserialize<MutationReportInput>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse report: {reportPath}");

        var survivors = report.Results
            .Where(r => r.Outcome.Equals("survived", StringComparison.OrdinalIgnoreCase))
            .Select(r => new Survivor
            {
                Id = r.Id,
                Strategy = r.Strategy,
                Description = r.Description,
                RiskLevel = r.RiskLevel,
                OriginalCode = r.OriginalCode,
                MutatedCode = r.MutatedCode,
                LineNumberHint = r.LineNumberHint
            })
            .ToList();

        if (onlyIds is not null)
        {
            var ids = onlyIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            survivors = survivors.Where(s => ids.Contains(s.Id)).ToList();
        }

        return survivors;
    }

    public static string GetSourceFile(string reportPath)
    {
        var json = File.ReadAllText(reportPath);
        var report = JsonSerializer.Deserialize<MutationReportInput>(json, JsonOptions);
        return report?.SourceFile ?? "";
    }
}
