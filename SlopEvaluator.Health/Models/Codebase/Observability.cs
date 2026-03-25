namespace SlopEvaluator.Health.Models;

/// <summary>
/// Logging, metrics, tracing, health checks, and alerting readiness.
/// </summary>
public sealed record Observability : IScoreable
{
    /// <summary>Score from 0.0 (worst) to 1.0 (best) for logging coverage.</summary>
    public required double LoggingCoverage { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for logging quality.</summary>
    public required double LoggingQuality { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for metrics instrumentation.</summary>
    public required double MetricsInstrumentation { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for distributed tracing coverage.</summary>
    public required double TracingCoverage { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for health check coverage.</summary>
    public required double HealthCheckCoverage { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for alerting readiness.</summary>
    public required double AlertingReadiness { get; init; }

    /// <summary>Logging framework details and statement analysis.</summary>
    public required LoggingProfile Logging { get; init; }

    /// <summary>Metrics framework and custom metric inventory.</summary>
    public required MetricsProfile Metrics { get; init; }

    /// <summary>Distributed tracing framework and endpoint coverage.</summary>
    public required TracingProfile Tracing { get; init; }

    /// <summary>Weighted composite score from 0.0 (worst) to 1.0 (best).</summary>
    public double Score => ScoreAggregator.WeightedAverage(
        (LoggingCoverage, 0.20),
        (LoggingQuality, 0.20),
        (MetricsInstrumentation, 0.15),
        (TracingCoverage, 0.15),
        (HealthCheckCoverage, 0.15),
        (AlertingReadiness, 0.15)
    );
}

/// <summary>
/// Logging framework configuration and statement analysis.
/// </summary>
public sealed record LoggingProfile
{
    /// <summary>Logging framework in use (e.g. "Serilog", "NLog").</summary>
    public required string Framework { get; init; }

    /// <summary>Whether structured (JSON/key-value) logging is enabled.</summary>
    public required bool StructuredLogging { get; init; }

    /// <summary>Whether correlation IDs are propagated across requests.</summary>
    public required bool CorrelationIdPropagation { get; init; }

    /// <summary>Total number of log statements in the codebase.</summary>
    public required int LogStatementsCount { get; init; }

    /// <summary>Log statement counts grouped by log level.</summary>
    public required Dictionary<string, int> StatementsByLevel { get; init; }

    /// <summary>Score from 0.0 (worst) to 1.0 (best) for sensitive data filtering in logs.</summary>
    public required double SensitiveDataFiltering { get; init; }
}

/// <summary>
/// Application metrics framework and custom metric inventory.
/// </summary>
public sealed record MetricsProfile
{
    /// <summary>Metrics framework in use (e.g. "Prometheus", "OpenTelemetry").</summary>
    public required string Framework { get; init; }

    /// <summary>Number of custom application metrics defined.</summary>
    public required int CustomMetricsCount { get; init; }

    /// <summary>Types of metrics defined (e.g. "Counter", "Histogram").</summary>
    public required List<string> MetricTypes { get; init; }

    /// <summary>Whether business-level metrics are defined.</summary>
    public required bool BusinessMetricsDefined { get; init; }
}

/// <summary>
/// Distributed tracing configuration and endpoint instrumentation.
/// </summary>
public sealed record TracingProfile
{
    /// <summary>Tracing framework in use (e.g. "OpenTelemetry", "Jaeger").</summary>
    public required string Framework { get; init; }

    /// <summary>Whether distributed tracing is enabled.</summary>
    public required bool DistributedTracingEnabled { get; init; }

    /// <summary>Number of endpoints with tracing instrumentation.</summary>
    public required int InstrumentedEndpoints { get; init; }

    /// <summary>Total number of endpoints in the application.</summary>
    public required int TotalEndpoints { get; init; }

    /// <summary>Whether span attributes contain rich contextual data.</summary>
    public required bool SpanAttributesRich { get; init; }
}
