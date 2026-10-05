namespace Aidp.Api.Models;

public enum ApplicationHealthEvidenceType { AzureMetric, ApplicationInsights, AppServiceLog, DeploymentMetadata, ResourceHealth, ConfigurationMetadata, TrustedAzureMonitorMetric, TrustedApplicationInsights, TrustedAzureResourceHealth, TrustedDeploymentMetadata }
public enum HealthMetricName { Requests, FailedRequests, Http5xx, Http4xx, AverageResponseTime, Dependencies, DependencyFailures, AverageDependencyDuration, CpuPercentage, CpuTime, MemoryWorkingSetBytes, Availability }
public enum HealthMetricAggregation { Count, Average, Minimum, Maximum, Total, Percentage }
public enum OverallHealthAssessment { Healthy, Degraded, Unhealthy, Unknown }
public enum ApplicationHealthCategory { Availability, Latency, HttpErrors, DependencyFailure, Cpu, Memory, Deployment, Configuration, Networking, PlatformHealth, Unknown }
public enum HealthSeverity { Informational, Low, Medium, High, Critical }
public enum HealthConclusionType { Symptom, Finding, LikelyCause, ConfirmedCause }
public enum HealthEvidenceProvenance { SuppliedAzureMetric, SuppliedApplicationInsights, SuppliedAppServiceLog, SuppliedDeploymentMetadata, SuppliedResourceHealth, SuppliedConfigurationMetadata, TrustedAzureMonitorMetric, TrustedApplicationInsights, TrustedAzureResourceHealth, TrustedDeploymentMetadata, PlatformKnownFact, TrustedPlatformKnowledge, ModelInference }
public enum HealthConfidenceLevel { Low, Medium, High }

public sealed record CreateApplicationHealthAnalysisRequest(
    string Question,
    ApplicationHealthTarget Target,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    IReadOnlyList<ApplicationHealthEvidenceInput> Evidence);

public sealed record ApplicationHealthTarget(
    string ApplicationName,
    string Environment,
    string? AzureService,
    string? ResourceName,
    string? ResourceGroup,
    string? Region);

public sealed record ApplicationHealthEvidenceInput(
    ApplicationHealthEvidenceType Type,
    DateTimeOffset? ObservedAt,
    string Title,
    string Content,
    IReadOnlyList<HealthMetricObservation>? Metrics);

public sealed record HealthMetricObservation(
    HealthMetricName Name,
    HealthMetricAggregation Aggregation,
    double Value,
    string Unit,
    DateTimeOffset? ObservedAt);

public sealed record SuppliedHealthEvidenceReference(
    string ApplicationName,
    string Environment,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    int EvidenceItemCount,
    bool RedactionsApplied);

public sealed record ApplicationHealthEvidence(
    HealthEvidenceProvenance Provenance,
    string Statement,
    string? SourceReference);

public sealed record HealthConfidence(HealthConfidenceLevel Level, string Rationale);

public sealed record ApplicationHealthFinding(
    string Finding,
    ApplicationHealthCategory Category,
    HealthSeverity Severity,
    HealthConclusionType ConclusionType,
    IReadOnlyList<ApplicationHealthEvidence> Evidence,
    HealthConfidence Confidence,
    string RecommendedInvestigation);

public sealed record ApplicationHealthAnalysisResponse(
    Guid AnalysisId,
    SuppliedHealthEvidenceReference SuppliedEvidence,
    string Summary,
    OverallHealthAssessment OverallHealthAssessment,
    IReadOnlyList<ApplicationHealthFinding> Findings,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> RecommendedNextSteps,
    HealthConfidence OverallConfidence,
    AiKnowledgeGrounding KnowledgeGrounding);

internal sealed record SanitizedHealthMetric(
    string Id, HealthMetricName Name, HealthMetricAggregation Aggregation, double Value, string Unit, DateTimeOffset? ObservedAt);

internal sealed record SanitizedHealthEvidence(
    string Id, ApplicationHealthEvidenceType Type, DateTimeOffset? ObservedAt, string Title, string Content,
    IReadOnlyList<SanitizedHealthMetric> Metrics);

internal sealed record SanitizedApplicationHealthInput(
    string Question, ApplicationHealthTarget Target, DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd,
    IReadOnlyList<SanitizedHealthEvidence> Evidence, bool RedactionsApplied);

internal sealed record ApplicationHealthModelOutput(
    string Summary,
    OverallHealthAssessment OverallHealthAssessment,
    IReadOnlyList<ApplicationHealthFinding> Findings,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> RecommendedNextSteps,
    HealthConfidence OverallConfidence);
