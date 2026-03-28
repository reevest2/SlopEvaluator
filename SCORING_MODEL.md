# SlopEvaluator Scoring Model

Complete reference for the codebase health scoring model — 68 leaf metrics across 14 dimensions, aggregated into a single 0.0–1.0 health score.

## Top-Level Aggregate

The final health score is a weighted average of 12 dimensions, computed in `Codebase.cs`:

| Dimension | Weight |
|---|---|
| Code Quality | 15% |
| Testing | 15% |
| Security | 10% |
| Dependencies | 8% |
| Requirements | 8% |
| CI/CD Pipeline | 8% |
| Observability | 7% |
| Developer Experience | 7% |
| Performance | 7% |
| Documentation | 5% |
| Team Process | 5% |
| AI Quality | 5% |

Architecture and Project Structure are scored separately and not included in the top-level aggregate.

---

## Dimension Breakdown

### 1. Code Quality (15%)

`CodeQuality.cs`

| Sub-metric | Weight |
|---|---|
| MaintainabilityIndex | 15% |
| CyclomaticComplexity | 15% |
| CodeDuplication | 15% |
| StyleConsistency | 10% |
| NullSafety | 15% |
| ErrorHandling | 15% |
| Readability | 15% |

Supporting data: ComplexityProfile (avg/max cyclomatic complexity, method sizes, nesting depth, god classes, hot spots), StyleAnalysis (analyzer warnings/errors, editorconfig, suppressions), CodeSmellSummary (long methods, design issues).

---

### 2. Testing (15%)

`TestingStrategy.cs`

| Sub-metric | Weight |
|---|---|
| LineCoverage | 15% |
| BranchCoverage | 20% |
| **MutationScore** | **30%** |
| EdgeCaseCoverage | 15% |
| TestQualityScore | 20% |

Supporting data: TestSuiteProfile (test counts, framework, determinism, flaky tests), TestQualityProfile (trustworthiness, precision, maintainability, diagnostic value), MutationTestingProfile (kill rate, survivors, patterns), TokenEfficiencyProfile (AI-assisted test token spend and ROI).

---

### 3. Security (10%)

`SecurityPosture.cs`

| Sub-metric | Weight |
|---|---|
| SecretHygiene | 20% |
| AuthPatterns | 15% |
| InputValidation | 15% |
| CryptographyPractice | 10% |
| DependencySecurity | 15% |
| HttpSecurity | 10% |
| DataProtection | 15% |

OWASP Top 10 (2021) coverage: A01 Broken Access Control, A02 Cryptographic Failures, A03 Injection, A04 Insecure Design, A05 Security Misconfiguration, A06 Vulnerable Components, A07 Authentication Failures, A08 Data Integrity Failures, A09 Logging/Monitoring Failures, A10 Server-Side Request Forgery.

---

### 4. Dependencies (8%)

`DependencyHealth.cs`

| Sub-metric | Weight |
|---|---|
| Freshness | 20% |
| **VulnerabilityFreedom** | **35%** |
| LicenseCompliance | 20% |
| TransitiveCleanliness | 15% |
| PackageCountScore | 10% |

Tracks: NuGet package staleness (major versions behind, days since update), known CVEs and vulnerability severity, license compliance (SPDX identifiers), deprecated packages and suggested alternatives.

---

### 5. Requirements (8%)

`RequirementsQuality.cs`

| Sub-metric | Weight |
|---|---|
| Clarity | 20% |
| Completeness | 20% |
| **Testability** | **25%** |
| Atomicity | 10% |
| AcceptanceCriteriaQuality | 15% |
| TraceabilityToCode | 10% |

Story assessment: ambiguous terms detection, missing scenarios identification, Given/When/Then format validation, estimated test count per story.

---

### 6. CI/CD Pipeline (8%)

`CiCdPipeline.cs`

| Sub-metric | Weight |
|---|---|
| BuildReliability | 20% |
| BuildSpeed | 10% |
| DeployFrequency | 15% |
| **PipelineCompleteness** | **25%** |
| EnvironmentParity | 15% |
| RollbackCapability | 15% |

DORA metrics tracked: Deployment Frequency, Lead Time for Changes, Mean Time to Recover, Change Failure Rate. Pipeline stages: Build, Unit Test, Integration Test, Security Scan, Code Analysis, Deploy, Smoke Test, Performance Test, Approval gates.

---

### 7. Observability (7%)

`Observability.cs`

| Sub-metric | Weight |
|---|---|
| LoggingCoverage | 20% |
| LoggingQuality | 20% |
| MetricsInstrumentation | 15% |
| TracingCoverage | 15% |
| HealthCheckCoverage | 15% |
| AlertingReadiness | 15% |

Tracks: logging frameworks (Serilog, NLog), structured logging, correlation IDs, metrics frameworks (Prometheus, OpenTelemetry), distributed tracing (OpenTelemetry, Jaeger) endpoint coverage, sensitive data filtering in logs.

---

### 8. Developer Experience (7%)

`DeveloperExperience.cs`

| Sub-metric | Weight |
|---|---|
| BuildTimeSatisfaction | 20% |
| TestRunSpeed | 15% |
| OnboardingFriction | 15% |
| ToolingMaturity | 20% |
| InnerLoopSpeed | 15% |
| DebugExperience | 15% |

Inner loop metrics: clean build time, incremental build time, unit test run time, hot reload support, dotnet watch mode, steps to first run. Tooling profile: .editorconfig, launchSettings.json, Directory.Build.props, Dockerfile, dev containers, git hooks.

---

### 9. Performance (7%)

`PerformanceProfile.cs`

| Sub-metric | Weight |
|---|---|
| StartupPerformance | 15% |
| MemoryEfficiency | 20% |
| **ResponseLatency** | **25%** |
| ThroughputCapacity | 15% |
| AllocationEfficiency | 15% |
| BundleSize | 10% |

Startup metrics: cold and warm start times, peak memory during startup, assemblies loaded. Runtime metrics: working set size, GC pressure (Gen 0/1/2), allocation rate, thread pool utilization. BenchmarkDotNet integration: mean/median latency, allocations, standard deviation, regression detection vs. baseline.

---

### 10. Documentation (5%)

`Documentation.cs`

| Sub-metric | Weight |
|---|---|
| ReadmeCompleteness | 20% |
| **ApiDocCoverage** | **25%** |
| AdrPresence | 10% |
| DocFreshness | 15% |
| InlineCommentQuality | 15% |
| OnboardingDocumentation | 15% |

Artifact inventory: README, CHANGELOG, CONTRIBUTING, CLAUDE.md, Architecture Decision Records (ADRs), XML documentation coverage on public members.

---

### 11. Team Process (5%)

`TeamProcessMetrics.cs`

| Sub-metric | Weight |
|---|---|
| PrCycleTimeHealth | 20% |
| ReviewQuality | 20% |
| KnowledgeDistribution | 15% |
| CommitHygiene | 15% |
| BranchStrategy | 10% |
| IncidentResponseHealth | 20% |

PR metrics: time to first review, time to merge, PR size distribution, review comments per PR, approval without comments rate. Commit metrics: conventional commit compliance, merge vs. rebase/squash strategy, force push frequency. Knowledge distribution: bus factor (min. contributors to lose knowledge), top contributor concentration, file ownership patterns, single-owner file percentage.

---

### 12. AI Quality (5%)

`AIInteractionQuality.cs`

| Sub-metric | Weight |
|---|---|
| **AverageEffectiveScore** | **25%** |
| AverageEfficiency | 20% |
| FirstPassSuccessRate | 20% |
| ContextLeverage | 15% |
| ImprovementTrend | 20% |

Tracking: interaction categories and effectiveness by category, iteration counts per interaction, token efficiency and cost, input dimension leverage ratios, score trends over time, top corrections applied.

---

## Separately Scored Dimensions

### Architecture

Defined as a nested class in `Codebase.cs`. Not included in the top-level aggregate.

| Sub-metric | Weight |
|---|---|
| LayerSeparation | 20% |
| DependencyDirection | 25% |
| CouplingScore | 25% |
| CohesionScore | 20% |
| AbstractionLevel | 10% |

**Penalty multiplier:** `Math.Max(0.5, 1.0 - CircularDependencies * 0.1)` — each circular dependency reduces the score by 10%, floored at 50%.

Detected patterns: Clean Architecture, Vertical Slice, Layered, Hexagonal, Microservices, Monolith, Modular Monolith, CQRS, Event-Driven.

---

### Project Structure

`ProjectStructure.cs`. Not included in the top-level aggregate.

| Sub-metric | Weight |
|---|---|
| NamingConsistency | 25% |
| **FolderOrganization** | **30%** |
| SolutionHygiene | 20% |
| ProjectGranularity | 25% |

Tracks: project count, source files, total LOC, project metadata (SDK type, output type, references).

---

## Scoring Mechanics

- **Aggregation:** All scores computed via `ScoreAggregator.WeightedAverage()` in `SlopEvaluator.Shared/Scoring/ScoreAggregator.cs`
- **Scale:** All metrics normalized to 0.0–1.0 (0.0 = worst, 1.0 = ideal)
- **Hierarchy:** Two-level weighted average — sub-metrics roll up to dimensions, dimensions roll up to the final score
- **Non-linear adjustments:** Only one in the entire model — the Architecture circular dependency penalty
- **Total leaf metrics:** 68 across 14 dimensions
