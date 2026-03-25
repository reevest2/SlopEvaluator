using System.Text.Json;
using SlopEvaluator.Mutations.Models;
using static SlopEvaluator.Mutations.Commands.CommandHelpers;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Engine;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Commands;

internal static class InitCommand
{
    internal static async Task<int> RunAsync(CliOptions opts)
    {
        var path = opts.PositionalArg1 ?? "mutations.json";
        var sample = new HarnessConfig
        {
            SourceFile = "src/MyProject/Services/Calculator.cs",
            ProjectPath = "src/MyProject/MyProject.csproj",
            TestCommand = "dotnet test tests/MyProject.Tests/MyProject.Tests.csproj --no-restore",
            Target = "MyProject.Services.Calculator.Add",
            TestTimeoutSeconds = 120,
            ReportPath = "mutation-report.json",
            Mutations =
            [
                new MutationSpec
                {
                    Id = "M01", Strategy = "boundary",
                    Description = "Change < to <= in range check",
                    OriginalCode = "if (value < maximum)",
                    MutatedCode = "if (value <= maximum)",
                    RiskLevel = "high", LineNumberHint = 42
                },
                new MutationSpec
                {
                    Id = "M02", Strategy = "return-value",
                    Description = "Return 0 instead of computed sum",
                    OriginalCode = "return a + b;",
                    MutatedCode = "return 0;",
                    RiskLevel = "high"
                },
                new MutationSpec
                {
                    Id = "M03", Strategy = "logic-inversion",
                    Description = "Negate null check guard clause",
                    OriginalCode = "if (input is null) throw new ArgumentNullException(nameof(input));",
                    MutatedCode = "// guard clause removed",
                    RiskLevel = "medium", LineNumberHint = 15
                }
            ]
        };

        var json = JsonSerializer.Serialize(sample, JsonOptions);
        await File.WriteAllTextAsync(path, json);
        Console.WriteLine($"Sample config written to: {path}");
        Console.WriteLine("Edit with your paths, then run: SlopEvaluator mutate " + path);
        return 0;
    }
}
