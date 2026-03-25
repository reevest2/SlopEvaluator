using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;
using SlopEvaluator.Mutations.Appliers;
using SlopEvaluator.Mutations.Generators;
using SlopEvaluator.Mutations.Analysis;

namespace SlopEvaluator.Mutations.Engine;

/// <summary>
/// Orchestrates the full mutation testing loop:
/// baseline → mutate → test → revert → report.
/// Optionally validates AI-recommended tests against survivors.
/// </summary>
public sealed class MutationEngine
{
    private readonly HarnessConfig _config;
    private readonly TestRunner _runner;
    private readonly Action<string> _log;
    private readonly MetricsCollector? _metrics;
    private readonly int _parallelism;
    private readonly bool _useRoslyn;
    private readonly ILogger? _logger;

    public MutationEngine(HarnessConfig config, Action<string>? log = null,
        MetricsCollector? metrics = null, int parallelism = 1, bool useRoslyn = false,
        ILogger? logger = null)
    {
        _config = config;
        _runner = new TestRunner(config.TestCommand, config.TestTimeoutSeconds);
        _log = log ?? Console.WriteLine;
        _metrics = metrics;
        _parallelism = Math.Max(1, parallelism);
        _useRoslyn = useRoslyn;
        _logger = logger;
    }

    private IMutationApplier CreateApplier() => _useRoslyn
        ? new RoslynMutationApplier(_config.SourceFile)
        : new MutationApplier(_config.SourceFile);

    public async Task<MutationReport> RunAsync(CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();
        _metrics?.RecordSessionStart("mutate", _config.SourceFile, _config.Target, _config.Mutations.Count);

        // ── Phase 0: Baseline ──────────────────────────────────────────
        _log("");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        _log("  PHASE 0 \u2014 BASELINE");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

        var baseline = await _runner.RunAsync(ct);

        if (!baseline.Success)
        {
            _log("  \u274c Baseline tests FAILED. Cannot proceed with mutation testing.");
            _log("  Fix your tests first, then rerun the harness.");
            _log("");
            _log("  Output:");
            _log(baseline.StandardOutput);
            if (!string.IsNullOrWhiteSpace(baseline.StandardError))
                _log(baseline.StandardError);

            throw new InvalidOperationException(
                "Baseline tests failed \u2014 mutation testing requires a green test suite.");
        }

        _log($"  \u2705 Baseline PASSED \u2014 {baseline.TestsRun} tests in {baseline.Duration.TotalSeconds:F1}s");
        _logger?.LogInformation("Baseline passed: {TestCount} tests in {Duration}s", baseline.TestsRun, baseline.Duration.TotalSeconds);
        _log("");

        _metrics?.RecordPhase("baseline", baseline.Duration.TotalMilliseconds, baseline.Success);
        _metrics?.RecordProcessExecution("test", "", baseline.ExitCode,
            baseline.Duration.TotalMilliseconds,
            System.Text.Encoding.UTF8.GetByteCount(baseline.StandardOutput),
            System.Text.Encoding.UTF8.GetByteCount(baseline.StandardError),
            baseline.PeakWorkingSetBytes);

        // ── Phase 1: Execute Mutations ─────────────────────────────────
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        _log("  PHASE 1 \u2014 EXECUTING MUTATIONS");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        _log($"  Source: {_config.SourceFile}");
        _log($"  Mutations: {_config.Mutations.Count}");
        if (_parallelism > 1)
            _log($"  Parallel:  {_parallelism} workers");
        _log($"  Est. time: ~{_config.Mutations.Count * baseline.Duration.TotalSeconds / _parallelism:F0}s");
        _log("");

        var results = _parallelism > 1
            ? await ExecuteMutationsParallel(baseline, ct)
            : await ExecuteMutationsSequential(baseline, ct);

        // ── Phase 2: Validate Recommended Tests (optional) ─────────────
        RecommendedTestResults? recommendedResults = null;

        if (_config.RecommendedTestFile is not null
            && File.Exists(_config.RecommendedTestFile))
        {
            _log("");
            _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
            _log("  PHASE 2 \u2014 VALIDATING RECOMMENDED TESTS");
            _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");

            var survivors = results
                .Where(r => r.Outcome == MutationOutcome.Survived)
                .ToList();

            if (survivors.Count > 0)
            {
                recommendedResults = await ValidateRecommendedTests(survivors, ct);
            }
            else
            {
                _log("  No survivors to test against \u2014 all mutants were killed.");
            }
        }

        // ── Phase 3: Report ────────────────────────────────────────────
        totalSw.Stop();

        var report = new MutationReport
        {
            SourceFile = _config.SourceFile,
            Target = _config.Target ?? Path.GetFileName(_config.SourceFile),
            RunDate = DateTime.UtcNow,
            TotalDuration = totalSw.Elapsed,
            BaselineDuration = baseline.Duration,
            BaselineTestCount = baseline.TestsRun,
            Results = results,
            RecommendedTestValidation = recommendedResults
        };

        PrintReport(report);

        _logger?.LogInformation("Mutation run complete: {Score}% score, {Killed} killed, {Survived} survived in {Duration}s",
            report.MutationScore, report.Killed, report.Survived, totalSw.Elapsed.TotalSeconds);

        _metrics?.RecordPhase("mutation-loop", totalSw.Elapsed.TotalMilliseconds, true);
        _metrics?.RecordAggregateStats(report, null, null, totalSw.Elapsed.TotalSeconds);

        return report;
    }

    private async Task<List<MutationResultEntry>> ExecuteMutationsSequential(
        TestRunResult baseline, CancellationToken ct)
    {
        var results = new List<MutationResultEntry>();

        using (var applier = CreateApplier())
        {
            for (int i = 0; i < _config.Mutations.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var mutation = _config.Mutations[i];
                _log($"  [{i + 1}/{_config.Mutations.Count}] {mutation.Id}: {mutation.Description}");

                var entry = await ExecuteSingleMutation(applier, mutation, baseline, ct);
                results.Add(entry);
                LogOutcome(entry);

                var k = results.Count(r => r.Outcome == MutationOutcome.Killed);
                var s = results.Count(r => r.Outcome == MutationOutcome.Survived);
                LogProgressBar(i + 1, _config.Mutations.Count, k, s);
            }

            // Verify restoration
            applier.Revert();
            if (!applier.VerifyOriginal())
            {
                _log("");
                _log("  \u26a0\ufe0f WARNING: Source file may be corrupted. Forcing restore from backup.");
                applier.ForceRestore();
            }

            _log("");
            _log("  Verifying clean restore...");
            var verification = await _runner.RunAsync(ct);
            if (!verification.Success)
            {
                _log("  \u26a0\ufe0f WARNING: Post-mutation baseline failed! Restoring from backup.");
                applier.ForceRestore();
            }
            else
            {
                _log("  \u2705 Source file verified \u2014 all tests pass on original code.");
            }
        }

        return results;
    }

    private async Task<List<MutationResultEntry>> ExecuteMutationsParallel(
        TestRunResult baseline, CancellationToken ct)
    {
        // Semaphore serializes the apply→build→test→revert cycle.
        // Workers prepare in parallel but take turns mutating the source file.
        var semaphore = new SemaphoreSlim(1, 1);
        var resultArray = new MutationResultEntry[_config.Mutations.Count];
        var completed = 0;
        var lockObj = new object();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, _config.Mutations.Count),
            new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = ct },
            async (i, token) =>
            {
                var mutation = _config.Mutations[i];

                // Serialize: only one worker applies/tests at a time
                await semaphore.WaitAsync(token);
                try
                {
                    using var applier = CreateApplier();
                    var entry = await ExecuteSingleMutation(applier, mutation, baseline, token);
                    resultArray[i] = entry;

                    var count = Interlocked.Increment(ref completed);
                    lock (lockObj)
                    {
                        _log($"  [{count}/{_config.Mutations.Count}] {mutation.Id}: {mutation.Description}");
                        LogOutcome(entry);

                        var k = resultArray.Count(r => r is { Outcome: MutationOutcome.Killed });
                        var s = resultArray.Count(r => r is { Outcome: MutationOutcome.Survived });
                        LogProgressBar(count, _config.Mutations.Count, k, s);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

        semaphore.Dispose();

        _log("");
        _log("  Verifying clean restore...");
        var verification = await _runner.RunAsync(ct);
        if (verification.Success)
            _log("  \u2705 Source file verified \u2014 all tests pass on original code.");
        else
            _log("  \u26a0\ufe0f WARNING: Post-parallel baseline check failed.");

        return [.. resultArray];
    }

    private void LogOutcome(MutationResultEntry entry)
    {
        var icon = entry.Outcome switch
        {
            MutationOutcome.Killed => "\U0001f5e1\ufe0f KILLED",
            MutationOutcome.Survived => "\U0001f6e1\ufe0f SURVIVED",
            MutationOutcome.CompileError => "\u274c COMPILE ERROR",
            MutationOutcome.Timeout => "\u23f1\ufe0f TIMEOUT",
            _ => "\u2753 ERROR"
        };
        _log($"           \u2192 {icon} ({entry.Duration.TotalSeconds:F1}s)");
    }

    private void LogProgressBar(int completed, int total, int killed, int survived)
    {
        const int barWidth = 30;
        var fraction = total > 0 ? (double)completed / total : 0;
        var filled = (int)(fraction * barWidth);
        var empty = barWidth - filled;

        var bar = new string('\u2588', filled) + new string('\u2591', empty);
        var pct = (fraction * 100).ToString("F0");
        _log($"  [{bar}] {pct}%  ({completed}/{total})  \U0001f5e1{killed} killed  \U0001f6e1{survived} survived");
    }

    private async Task<MutationResultEntry> ExecuteSingleMutation(
        IMutationApplier applier,
        MutationSpec mutation,
        TestRunResult baseline,
        CancellationToken ct)
    {
        _logger?.LogDebug("Mutation start: {MutationId} [{Strategy}]", mutation.Id, mutation.Strategy);
        var sw = Stopwatch.StartNew();

        // Apply
        var applySw = Stopwatch.StartNew();
        var applyResult = applier.Apply(mutation);
        applySw.Stop();

        if (!applyResult.Success)
        {
            sw.Stop();
            applier.Revert();
            RecordMutationMetric(mutation, "error", sw.Elapsed,
                applySw.Elapsed, null, null, false, 0, 0);
            return MakeEntry(mutation, MutationOutcome.Error, sw.Elapsed,
                errorOutput: $"Apply failed: {applyResult.Error}");
        }

        // Test — `dotnet test` handles incremental build internally.
        // Skipping the explicit BuildAsync() call saves ~6-8s per mutation.
        // Compile errors are detected from test output (build failure = non-zero exit + error text).
        var testResult = await _runner.RunAsync(ct);
        sw.Stop();
        applier.Revert();

        _metrics?.RecordProcessExecution("test", mutation.Id, testResult.ExitCode,
            testResult.Duration.TotalMilliseconds,
            System.Text.Encoding.UTF8.GetByteCount(testResult.StandardOutput),
            System.Text.Encoding.UTF8.GetByteCount(testResult.StandardError),
            testResult.PeakWorkingSetBytes);

        // Detect compile errors from test output (build failures surface as test failures
        // with build error text in stderr). Check for common build error patterns.
        if (!testResult.Success && IsCompileError(testResult))
        {
            RecordMutationMetric(mutation, "compileError", sw.Elapsed,
                applySw.Elapsed, null, testResult.Duration,
                true, 0, 0);
            return MakeEntry(mutation, MutationOutcome.CompileError, sw.Elapsed,
                errorOutput: TruncateOutput(testResult.StandardError + testResult.StandardOutput, 2000));
        }

        // Check for timeout — configurable multiplier, with absolute floor.
        var timeoutThreshold = TimeSpan.FromSeconds(
            Math.Max(baseline.Duration.TotalSeconds * _config.TimeoutMultiplier,
                     _config.AbsoluteTimeoutFloorSeconds));
        if (testResult.Duration > timeoutThreshold)
        {
            RecordMutationMetric(mutation, "timeout", sw.Elapsed,
                applySw.Elapsed, null, testResult.Duration,
                true, testResult.TestsRun, 0);
            return MakeEntry(mutation, MutationOutcome.Timeout, sw.Elapsed,
                testsRun: testResult.TestsRun);
        }

        // Killed = at least one test failed
        var outcome = testResult.Success
            ? MutationOutcome.Survived
            : MutationOutcome.Killed;

        RecordMutationMetric(mutation, outcome.ToString().ToLowerInvariant(), sw.Elapsed,
            applySw.Elapsed, null, testResult.Duration,
            true, testResult.TestsRun, testResult.TestsFailed);

        _logger?.LogInformation("Mutation end: {MutationId} => {Outcome} in {Duration}s",
            mutation.Id, outcome, sw.Elapsed.TotalSeconds);

        return MakeEntry(mutation, outcome, sw.Elapsed,
            testsRun: testResult.TestsRun,
            testsFailed: testResult.TestsFailed,
            failedTestNames: testResult.FailedTestNames);
    }

    private void RecordMutationMetric(MutationSpec mutation, string outcome,
        TimeSpan total, TimeSpan? apply, TimeSpan? build, TimeSpan? test,
        bool applySuccess, int testsRun, int testsFailed)
    {
        _metrics?.RecordMutation(mutation.Id, mutation.Strategy, mutation.RiskLevel,
            outcome, total.TotalMilliseconds,
            apply?.TotalMilliseconds, build?.TotalMilliseconds, test?.TotalMilliseconds,
            applySuccess, testsRun, testsFailed);
    }

    private async Task<RecommendedTestResults> ValidateRecommendedTests(
        List<MutationResultEntry> survivors, CancellationToken ct)
    {
        _log($"  Recommended test file: {_config.RecommendedTestFile}");
        _log($"  Survivors to validate against: {survivors.Count}");
        _log("");

        // Step 1: Verify recommended tests pass on original code
        _log("  Running recommended tests against original code...");
        var originalResult = await _runner.RunAsync(ct);
        bool passesOnOriginal = originalResult.Success;

        if (!passesOnOriginal)
        {
            _log("  \u274c Recommended tests FAIL on original code \u2014 they have bugs.");
            _log("  Skipping mutant validation.");
            return new RecommendedTestResults
            {
                TestFile = _config.RecommendedTestFile!,
                PassesOnOriginal = false,
                MutantResults = []
            };
        }

        _log("  \u2705 Recommended tests pass on original code.");
        _log("");

        // Step 2: For each survivor, apply the mutation and run tests
        var mutantResults = new List<RecommendedTestVsMutant>();

        using (var applier = CreateApplier())
        {
            foreach (var survivor in survivors)
            {
                ct.ThrowIfCancellationRequested();

                var mutation = _config.Mutations.First(m => m.Id == survivor.Id);
                _log($"  Testing recommended tests vs {mutation.Id}: {mutation.Description}");

                var applyResult = applier.Apply(mutation);
                if (!applyResult.Success)
                {
                    mutantResults.Add(new RecommendedTestVsMutant
                    {
                        MutationId = mutation.Id,
                        NowKilled = false,
                        Details = $"Could not apply mutation: {applyResult.Error}"
                    });
                    applier.Revert();
                    continue;
                }

                var recBuild = await _runner.BuildAsync(_config.ProjectPath, ct);
                if (!recBuild.Success)
                {
                    mutantResults.Add(new RecommendedTestVsMutant
                    {
                        MutationId = mutation.Id,
                        NowKilled = true,
                        Details = "Mutation now causes compile error with new tests"
                    });
                    applier.Revert();
                    continue;
                }

                var testResult = await _runner.RunAsync(ct);
                applier.Revert();

                bool nowKilled = !testResult.Success;
                var icon = nowKilled ? "\U0001f5e1\ufe0f NOW KILLED" : "\U0001f6e1\ufe0f STILL SURVIVES";
                _log($"           \u2192 {icon}");

                mutantResults.Add(new RecommendedTestVsMutant
                {
                    MutationId = mutation.Id,
                    NowKilled = nowKilled,
                    Details = nowKilled
                        ? $"Failed tests: {testResult.FailedTestNames}"
                        : "Recommended tests did not catch this mutation"
                });
            }

            applier.Revert();
        }

        return new RecommendedTestResults
        {
            TestFile = _config.RecommendedTestFile!,
            PassesOnOriginal = true,
            MutantResults = mutantResults
        };
    }

    private void PrintReport(MutationReport report)
    {
        _log("");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        _log($"  MUTATION TEST REPORT \u2014 {report.Target}");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        _log("");
        _log($"  Mutation Score:  {report.MutationScore:F1}%");
        _log("");
        _log($"  \U0001f5e1\ufe0f Killed:         {report.Killed}");
        _log($"  \U0001f6e1\ufe0f Survived:       {report.Survived}  \u2190 weak spots");
        _log($"  \u274c Compile Error:   {report.CompileErrors}");
        _log($"  \u23f1\ufe0f Timeout:         {report.Timeouts}");
        _log("");
        _log($"  Baseline:  {report.BaselineTestCount} tests in {report.BaselineDuration.TotalSeconds:F1}s");
        _log($"  Total run: {report.TotalDuration.TotalSeconds:F1}s");
        _log("");

        // Detail on survivors
        var survivors = report.Results
            .Where(r => r.Outcome == MutationOutcome.Survived)
            .OrderByDescending(r => r.RiskLevel == "high" ? 3 : r.RiskLevel == "medium" ? 2 : 1)
            .ToList();

        if (survivors.Count > 0)
        {
            _log("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
            _log("  SURVIVING MUTANTS (test gaps)");
            _log("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");

            foreach (var s in survivors)
            {
                _log("");
                _log($"  \U0001f6e1\ufe0f {s.Id} [{s.RiskLevel.ToUpperInvariant()}]");
                _log($"     Strategy:    {s.Strategy}");
                _log($"     Description: {s.Description}");
                if (s.OriginalCode is not null && s.MutatedCode is not null)
                {
                    var line = s.LineNumberHint.HasValue ? $" (line {s.LineNumberHint})" : "";
                    _log($"     Diff{line}:");
                    _log($"       \u2212 {Truncate(s.OriginalCode, 80)}");
                    _log($"       + {Truncate(s.MutatedCode, 80)}");
                }
            }
        }

        // Recommended test validation
        if (report.RecommendedTestValidation is { } rec)
        {
            _log("");
            _log("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
            _log("  AI-RECOMMENDED TEST VALIDATION");
            _log("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
            _log($"  Passes on original: {(rec.PassesOnOriginal ? "\u2705 Yes" : "\u274c No")}");

            if (rec.PassesOnOriginal)
            {
                _log($"  Mutants now killed:      {rec.MutantsNowKilled}");
                _log($"  Mutants still surviving: {rec.MutantsStillSurviving}");
                _log("");

                foreach (var m in rec.MutantResults)
                {
                    var icon = m.NowKilled ? "\U0001f5e1\ufe0f" : "\U0001f6e1\ufe0f";
                    _log($"  {icon} {m.MutationId}: {m.Details}");
                }
            }
        }

        // Survivor pattern analysis — actionable recommendations
        var recommendations = SurvivorAnalyzer.Analyze(report);
        report.Recommendations = recommendations.Count > 0 ? recommendations : null;
        if (recommendations.Count > 0)
        {
            _log(SurvivorAnalyzer.FormatRecommendations(recommendations));
        }

        _log("");
        _log("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
    }

    private static MutationResultEntry MakeEntry(
        MutationSpec mutation,
        MutationOutcome outcome,
        TimeSpan duration,
        string? errorOutput = null,
        int testsRun = 0,
        int testsFailed = 0,
        string? failedTestNames = null)
    {
        return new MutationResultEntry
        {
            Id = mutation.Id,
            Strategy = mutation.Strategy,
            Description = mutation.Description,
            RiskLevel = mutation.RiskLevel,
            Outcome = outcome,
            Duration = duration,
            OriginalCode = mutation.OriginalCode,
            MutatedCode = mutation.MutatedCode,
            LineNumberHint = mutation.LineNumberHint,
            ErrorOutput = errorOutput,
            TestsRun = testsRun,
            TestsFailed = testsFailed,
            FailedTestNames = failedTestNames
        };
    }

    /// <summary>
    /// Detects if a test failure was actually a compile error (build failed during dotnet test).
    /// Since we skip the explicit build step, compile errors surface as test failures.
    /// </summary>
    private static bool IsCompileError(TestRunResult result)
    {
        var combined = result.StandardError + result.StandardOutput;
        return result.TestsRun == 0
            && (combined.Contains("error CS", StringComparison.Ordinal)
                || combined.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("error MSB", StringComparison.Ordinal)
                || combined.Contains("error NU", StringComparison.Ordinal));
    }

    private static string TruncateOutput(string output, int maxLength)
    {
        if (output.Length <= maxLength) return output;
        return output[..maxLength] + "\n... [truncated]";
    }

    private static string Truncate(string text, int maxLength)
    {
        var oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= maxLength ? oneLine : oneLine[..maxLength] + "...";
    }
}
