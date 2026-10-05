namespace Aidp.Api.Models;

public enum AzureMetricProfile { AppServiceBasicHealth, AppServiceHttpHealth, AppServiceResourcePressure }
public enum AzureMetricTimeWindow { Last30Minutes, Last2Hours, Last24Hours }
public enum AzureMetricCollectionStatus { Success, NoData, Partial, Unavailable, Unauthorized, Timeout }
public enum ApplicationEvidenceSource { AzureMonitorMetrics }

public sealed record CreateLiveApplicationHealthAnalysisRequest(
    string ApplicationName, string Question, AzureMetricProfile MetricProfile = AzureMetricProfile.AppServiceBasicHealth,
    AzureMetricTimeWindow TimeWindow = AzureMetricTimeWindow.Last30Minutes,
    LiveHealthAnalysisProfile AnalysisProfile = LiveHealthAnalysisProfile.AzureMonitorMetrics);

public sealed record AzureMetricCollectionMetadata(
    ApplicationEvidenceSource Collector, AzureMetricProfile MetricProfile, AzureMetricTimeWindow TimeWindow,
    AzureMetricCollectionStatus Status, long DurationMilliseconds, int RequestedMetricCount, int CollectedMetricCount);

public sealed record LiveApplicationHealthAnalysisResponse(
    AzureMetricCollectionMetadata Collection,
    ApplicationInsightsCollectionMetadata? ApplicationInsightsCollection,
    ResourceHealthCollectionMetadata? ResourceHealthCollection,
    DeploymentMetadataCollectionMetadata? DeploymentMetadataCollection,
    ApplicationHealthAnalysisResponse? Analysis);

internal enum ApplicationResourceType { AppService }
internal enum TrustedEvidenceSource { AzureMonitorMetrics, ApplicationInsights, AzureResourceHealth }

internal sealed record ApplicationResourceDescriptor(
    string ApplicationName, string Environment, string ResourceId,
    string? ApplicationInsightsResourceId, string? ApplicationInsightsComponentName,
    string? LogAnalyticsWorkspaceId,
    ApplicationResourceType ResourceType,
    IReadOnlySet<TrustedEvidenceSource> AllowedEvidenceSources);

internal sealed record AzureMetricCollectionRequest(
    ApplicationResourceDescriptor Resource, AzureMetricProfile MetricProfile, AzureMetricTimeWindow TimeWindow);

internal sealed record CollectedAzureMetric(
    string EvidenceId, HealthMetricName Name, HealthMetricAggregation Aggregation,
    double? Value, string Unit, DateTimeOffset? ObservedAt, AzureMetricCollectionStatus Status);

internal sealed record AzureMetricCollectionResult(
    AzureMetricCollectionMetadata Metadata, IReadOnlyList<CollectedAzureMetric> Metrics);
