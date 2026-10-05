namespace Aidp.Api.Models;

public enum LiveHealthAnalysisProfile { AzureMonitorMetrics, AppServiceWithApplicationInsights }
public enum ApplicationInsightsQueryProfile { ApplicationRequests, ApplicationDependencies, ApplicationFailures, ApplicationBasicHealth }
public enum ApplicationInsightsCollectionStatus { Success, NoData, Partial, Unauthorized, Timeout, Unavailable, QueryFailed }
public enum ApplicationInsightsSignal
{
    RequestCount,
    FailedRequestCount,
    AverageRequestDuration,
    P95RequestDuration,
    DependencyCount,
    FailedDependencyCount,
    AverageDependencyDuration,
    FailedRequestSummary,
    FailedDependencySummary
}

public sealed record ApplicationInsightsCollectionMetadata(
    string Collector,
    ApplicationInsightsQueryProfile QueryProfile,
    AzureMetricTimeWindow TimeWindow,
    ApplicationInsightsCollectionStatus Status,
    int QueryCount,
    int ReturnedSummaryCount,
    long DurationMilliseconds);

internal sealed record ApplicationInsightsCollectionRequest(
    ApplicationResourceDescriptor Resource,
    ApplicationInsightsQueryProfile QueryProfile,
    AzureMetricTimeWindow TimeWindow);

internal sealed record ApplicationInsightsSummary(
    string EvidenceId,
    ApplicationInsightsSignal Signal,
    double? Value,
    string Unit,
    string? OperationName,
    string? ResultCode,
    ApplicationInsightsCollectionStatus Status);

internal sealed record ApplicationInsightsCollectionResult(
    ApplicationInsightsCollectionMetadata Metadata,
    IReadOnlyList<ApplicationInsightsSummary> Summaries);
