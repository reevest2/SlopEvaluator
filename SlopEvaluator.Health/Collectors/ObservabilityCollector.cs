using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Detects logging, metrics, tracing, and health check infrastructure.
/// </summary>
public class ObservabilityCollector
{
    private readonly ILogger<ObservabilityCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservabilityCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public ObservabilityCollector(ILogger<ObservabilityCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<ObservabilityCollector>.Instance;
    }

    /// <summary>
    /// Detect logging, metrics, tracing, and health check infrastructure in a project.
    /// </summary>
    public async Task<Observability> CollectAsync(string projectPath)
    {
        _logger.LogInformation("Starting observability scan for {ProjectPath}", projectPath);
        var csFiles = GetSourceFiles(projectPath);

        int totalMethods = 0;
        int methodsWithLogging = 0;
        int logStatements = 0;
        var logLevels = new Dictionary<string, int>
        {
            ["Debug"] = 0, ["Information"] = 0, ["Warning"] = 0, ["Error"] = 0, ["Critical"] = 0
        };
        string logFramework = "none";
        bool structuredLogging = false;
        bool correlationId = false;
        int customMetrics = 0;
        string metricsFramework = "none";
        var metricTypes = new List<string>();
        bool businessMetrics = false;
        string tracingFramework = "none";
        bool distributedTracing = false;
        int instrumentedEndpoints = 0;
        int totalEndpoints = 0;
        bool spanAttributes = false;
        int healthChecks = 0;
        bool alerting = false;

        foreach (var file in csFiles)
        {
            var content = await File.ReadAllTextAsync(file);

            // Detect logging framework
            if (content.Contains("Serilog")) logFramework = "Serilog";
            else if (content.Contains("NLog")) logFramework = "NLog";
            else if (content.Contains("ILogger")) logFramework = "ILogger";

            // Count log statements
            var logMethodPattern = new Regex(@"\.(LogDebug|LogInformation|LogWarning|LogError|LogCritical|Log\.Debug|Log\.Information|Log\.Warning|Log\.Error|Log\.Fatal)\(");
            var logMatches = logMethodPattern.Matches(content);
            logStatements += logMatches.Count;

            if (logMatches.Count > 0)
                methodsWithLogging++;

            foreach (Match m in logMatches)
            {
                var method = m.Groups[1].Value;
                if (method.Contains("Debug")) logLevels["Debug"]++;
                else if (method.Contains("Information")) logLevels["Information"]++;
                else if (method.Contains("Warning")) logLevels["Warning"]++;
                else if (method.Contains("Error")) logLevels["Error"]++;
                else if (method.Contains("Critical") || method.Contains("Fatal")) logLevels["Critical"]++;
            }

            // Structured logging detection
            if (content.Contains("LogInformation(\"{") || content.Contains("LogError(\"{")
                || content.Contains(".ForContext") || content.Contains("Enrich."))
                structuredLogging = true;

            // Correlation ID
            if (content.Contains("CorrelationId") || content.Contains("correlation-id")
                || content.Contains("X-Request-Id") || content.Contains("TraceIdentifier"))
                correlationId = true;

            // Metrics
            if (content.Contains("OpenTelemetry")) metricsFramework = "OpenTelemetry";
            else if (content.Contains("Prometheus")) metricsFramework = "Prometheus";
            else if (content.Contains("ApplicationInsights")) metricsFramework = "AppInsights";

            if (content.Contains("Counter<") || content.Contains("CreateCounter"))
            { customMetrics++; if (!metricTypes.Contains("Counter")) metricTypes.Add("Counter"); }
            if (content.Contains("Histogram<") || content.Contains("CreateHistogram"))
            { customMetrics++; if (!metricTypes.Contains("Histogram")) metricTypes.Add("Histogram"); }
            if (content.Contains("Gauge") || content.Contains("ObservableGauge"))
            { customMetrics++; if (!metricTypes.Contains("Gauge")) metricTypes.Add("Gauge"); }

            if (content.Contains("orders") || content.Contains("revenue") || content.Contains("signup")
                || content.Contains("conversion"))
                businessMetrics = true;

            // Tracing
            if (content.Contains("ActivitySource") || content.Contains("AddOpenTelemetryTracing")
                || content.Contains("StartActivity"))
            {
                tracingFramework = "OpenTelemetry";
                distributedTracing = true;
            }
            if (content.Contains("SetTag") || content.Contains("AddTag"))
                spanAttributes = true;

            // Endpoints
            if (content.Contains("[HttpGet]") || content.Contains("[HttpPost]")
                || content.Contains("MapGet") || content.Contains("MapPost"))
                totalEndpoints++;

            // Health checks
            if (content.Contains("AddHealthChecks") || content.Contains("IHealthCheck")
                || content.Contains("MapHealthChecks"))
                healthChecks++;

            // Alerting
            if (content.Contains("AlertRule") || content.Contains("PagerDuty")
                || content.Contains("alertmanager"))
                alerting = true;

            totalMethods++;
        }

        _logger.LogDebug("Observability scan complete: {FileCount} files analyzed, {HealthChecks} health checks found", csFiles.Count, healthChecks);
        _logger.LogInformation("Observability scan detected logging framework: {Framework}, total log statements: {LogCount}", logFramework, logStatements);
        double loggingCoverage = totalMethods > 0
            ? Math.Min(1.0, (double)methodsWithLogging / totalMethods * 5) // scale up — not every file needs logging
            : 0.0;
        double loggingQuality = ScoreLoggingQuality(logFramework, structuredLogging, correlationId, logLevels);
        double metricsScore = ScoreMetrics(customMetrics, metricsFramework);
        double tracingScore = distributedTracing ? 0.8 : 0.0;
        double healthCheckScore = healthChecks > 0 ? Math.Min(1.0, healthChecks / 3.0) : 0.0;
        double alertingScore = alerting ? 0.8 : 0.0;

        _logger.LogInformation("Observability scores — LoggingCoverage: {Coverage:F3}, MetricsScore: {Metrics:F3}, TracingScore: {Tracing:F3}", loggingCoverage, metricsScore, tracingScore);

        return new Observability
        {
            LoggingCoverage = loggingCoverage,
            LoggingQuality = loggingQuality,
            MetricsInstrumentation = metricsScore,
            TracingCoverage = tracingScore,
            HealthCheckCoverage = healthCheckScore,
            AlertingReadiness = alertingScore,
            Logging = new LoggingProfile
            {
                Framework = logFramework,
                StructuredLogging = structuredLogging,
                CorrelationIdPropagation = correlationId,
                LogStatementsCount = logStatements,
                StatementsByLevel = logLevels,
                SensitiveDataFiltering = 0.5 // would need deeper analysis
            },
            Metrics = new MetricsProfile
            {
                Framework = metricsFramework,
                CustomMetricsCount = customMetrics,
                MetricTypes = metricTypes,
                BusinessMetricsDefined = businessMetrics
            },
            Tracing = new TracingProfile
            {
                Framework = tracingFramework,
                DistributedTracingEnabled = distributedTracing,
                InstrumentedEndpoints = instrumentedEndpoints,
                TotalEndpoints = totalEndpoints,
                SpanAttributesRich = spanAttributes
            }
        };
    }

    internal static double ScoreLoggingQuality(string framework, bool structured, bool correlationId,
        Dictionary<string, int> levels)
    {
        double score = 0;
        if (framework != "none") score += 0.3;
        if (structured) score += 0.3;
        if (correlationId) score += 0.2;
        // Good level distribution: not all errors, not all debug
        int totalLogs = levels.Values.Sum();
        if (totalLogs > 0)
        {
            bool hasMultipleLevels = levels.Values.Count(v => v > 0) >= 3;
            if (hasMultipleLevels) score += 0.2;
        }
        return Math.Min(1.0, score);
    }

    internal static double ScoreMetrics(int customMetrics, string framework)
    {
        if (framework == "none" && customMetrics == 0) return 0.0;
        double base_score = framework != "none" ? 0.4 : 0.0;
        base_score += Math.Min(0.6, customMetrics * 0.1);
        return Math.Min(1.0, base_score);
    }

    private static List<string> GetSourceFiles(string path) =>
        Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(f => {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
                    && !rel.Contains("benchmarks/")
                    && !rel.Contains(".claude/worktrees/");
            })
            .ToList();
}
