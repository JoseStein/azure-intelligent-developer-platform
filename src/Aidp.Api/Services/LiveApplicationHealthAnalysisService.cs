using Aidp.Api.Models;

namespace Aidp.Api.Services;

internal sealed class UnknownApplicationException : Exception;

internal interface ILiveApplicationHealthAnalysisService
{
    Task<LiveApplicationHealthAnalysisResponse> AnalyzeAsync(
        CreateLiveApplicationHealthAnalysisRequest request, CancellationToken cancellationToken);
}

internal sealed class LiveApplicationHealthAnalysisService(
    IApplicationResourceCatalog catalog, IAzureMonitorMetricsCollector collector,
    IApplicationInsightsHealthCollector applicationInsightsCollector,
    IApplicationHealthAnalysisService analysis, TimeProvider timeProvider,
    IAzureResourceHealthCollector? resourceHealthCollector = null,
    IAzureDeploymentMetadataCollector? deploymentCollector = null) : ILiveApplicationHealthAnalysisService
{
    public async Task<LiveApplicationHealthAnalysisResponse> AnalyzeAsync(
        CreateLiveApplicationHealthAnalysisRequest request, CancellationToken cancellationToken)
    {
        if (!catalog.TryResolve(request.ApplicationName, out var resource) ||
            resource.Environment != "dev" || resource.ResourceType != ApplicationResourceType.AppService ||
            !resource.AllowedEvidenceSources.Contains(TrustedEvidenceSource.AzureMonitorMetrics))
            throw new UnknownApplicationException();

        var collection = await collector.CollectAsync(
            new(resource, request.MetricProfile, request.TimeWindow), cancellationToken);
        if (collection.Metadata.Status is AzureMetricCollectionStatus.Unavailable or
            AzureMetricCollectionStatus.Unauthorized or AzureMetricCollectionStatus.Timeout)
            return new(collection.Metadata, null, null, null, null);

        ApplicationInsightsCollectionResult? applicationInsights = null;
        if (request.AnalysisProfile == LiveHealthAnalysisProfile.AppServiceWithApplicationInsights)
        {
            if (!resource.AllowedEvidenceSources.Contains(TrustedEvidenceSource.ApplicationInsights) ||
                string.IsNullOrWhiteSpace(resource.ApplicationInsightsResourceId))
                throw new UnknownApplicationException();
            applicationInsights = await applicationInsightsCollector.CollectAsync(
                new(resource, ApplicationInsightsQueryProfile.ApplicationBasicHealth, request.TimeWindow), cancellationToken);
            if (applicationInsights.Metadata.Status is ApplicationInsightsCollectionStatus.Unavailable or
                ApplicationInsightsCollectionStatus.Unauthorized or ApplicationInsightsCollectionStatus.Timeout or
                ApplicationInsightsCollectionStatus.QueryFailed)
                return new(collection.Metadata, applicationInsights.Metadata, null, null, null);
        }

        ResourceHealthCollectionResult? resourceHealth = null;
        if (resourceHealthCollector is not null && resource.AllowedEvidenceSources.Contains(TrustedEvidenceSource.AzureResourceHealth))
            resourceHealth = await resourceHealthCollector.CollectAsync(resource, cancellationToken);
        var deployment = deploymentCollector is null ? null : await deploymentCollector.CollectAsync(resource, cancellationToken);

        var end = timeProvider.GetUtcNow();
        var start = end - (request.TimeWindow switch
        {
            AzureMetricTimeWindow.Last30Minutes => TimeSpan.FromMinutes(30),
            AzureMetricTimeWindow.Last2Hours => TimeSpan.FromHours(2),
            AzureMetricTimeWindow.Last24Hours => TimeSpan.FromHours(24),
            _ => throw new ArgumentOutOfRangeException(nameof(request.TimeWindow))
        });
        var evidence = collection.Metrics.Select(metric => new ApplicationHealthEvidenceInput(
            ApplicationHealthEvidenceType.TrustedAzureMonitorMetric, metric.ObservedAt,
            $"Azure Monitor metric: {metric.Name}",
            metric.Status == AzureMetricCollectionStatus.NoData
                ? $"Azure Monitor returned no samples for {metric.Name} in the controlled observation window."
                : $"Azure Monitor returned a collected {metric.Aggregation} value for {metric.Name} in the controlled observation window.",
            metric.Value is { } value
                ? [new(metric.Name, metric.Aggregation, value, metric.Unit, metric.ObservedAt)] : [])).ToList();
        if (applicationInsights is not null)
            evidence.AddRange(applicationInsights.Summaries.Select(ToEvidence));
        if (resourceHealth is not null && resourceHealth.Metadata.Status == ResourceHealthCollectionStatus.Success)
            evidence.Add(new(ApplicationHealthEvidenceType.TrustedAzureResourceHealth, resourceHealth.ObservedAt,
                "Azure Resource Health availability", resourceHealth.Summary ?? $"Azure Resource Health reported {resourceHealth.Metadata.Availability}.", []));
        if (deployment?.Metadata.Status == DeploymentMetadataCollectionStatus.Success)
            evidence.Add(new(ApplicationHealthEvidenceType.TrustedDeploymentMetadata, deployment.Timestamp,
                "Trusted deployment metadata", $"Trusted deployment metadata reports a deployment with status {deployment.Status ?? "unknown"}; timing is correlation only.", []));
        var healthRequest = new CreateApplicationHealthAnalysisRequest(request.Question,
            new(resource.ApplicationName, resource.Environment, "appservice", null, null, null),
            start, end, evidence);
        return new(collection.Metadata, applicationInsights?.Metadata,
            resourceHealth?.Metadata, deployment?.Metadata, await analysis.AnalyzeAsync(healthRequest, cancellationToken));
    }

    private static ApplicationHealthEvidenceInput ToEvidence(ApplicationInsightsSummary summary)
    {
        var metric = Metric(summary.Signal);
        var status = summary.Status == ApplicationInsightsCollectionStatus.NoData
            ? "Application Insights returned no aggregate value for this controlled summary."
            : summary.Signal is ApplicationInsightsSignal.FailedRequestSummary or ApplicationInsightsSignal.FailedDependencySummary
                ? $"Application Insights returned a bounded failure summary with result code {summary.ResultCode ?? "unknown"} and count {summary.Value}."
                : $"Application Insights returned the controlled aggregate {summary.Signal} value {summary.Value} {summary.Unit}.";
        return new(ApplicationHealthEvidenceType.TrustedApplicationInsights, null,
            $"Application Insights summary: {summary.Signal}", status,
            summary.Value is { } value && metric is { } definition
                ? [new(definition.Name, definition.Aggregation, value, summary.Unit, null)] : []);
    }

    private static (HealthMetricName Name, HealthMetricAggregation Aggregation)? Metric(ApplicationInsightsSignal signal) => signal switch
    {
        ApplicationInsightsSignal.RequestCount => (HealthMetricName.Requests, HealthMetricAggregation.Count),
        ApplicationInsightsSignal.FailedRequestCount => (HealthMetricName.FailedRequests, HealthMetricAggregation.Count),
        ApplicationInsightsSignal.AverageRequestDuration => (HealthMetricName.AverageResponseTime, HealthMetricAggregation.Average),
        ApplicationInsightsSignal.P95RequestDuration => null,
        ApplicationInsightsSignal.DependencyCount => (HealthMetricName.Dependencies, HealthMetricAggregation.Count),
        ApplicationInsightsSignal.FailedDependencyCount => (HealthMetricName.DependencyFailures, HealthMetricAggregation.Count),
        ApplicationInsightsSignal.AverageDependencyDuration => (HealthMetricName.AverageDependencyDuration, HealthMetricAggregation.Average),
        _ => null
    };
}

internal static class LiveApplicationHealthAnalysisValidator
{
    internal static Dictionary<string, string[]> Validate(CreateLiveApplicationHealthAnalysisRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.ApplicationName) || request.ApplicationName.Length is < 3 or > 100)
            errors[nameof(request.ApplicationName)] = ["ApplicationName must be between 3 and 100 characters."];
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length is < 20 or > 1000)
            errors[nameof(request.Question)] = ["Question must be between 20 and 1000 characters."];
        if (!Enum.IsDefined(request.MetricProfile)) errors[nameof(request.MetricProfile)] = ["MetricProfile is invalid."];
        if (!Enum.IsDefined(request.TimeWindow)) errors[nameof(request.TimeWindow)] = ["TimeWindow is invalid."];
        if (!Enum.IsDefined(request.AnalysisProfile)) errors[nameof(request.AnalysisProfile)] = ["AnalysisProfile is invalid."];
        return errors;
    }
}
