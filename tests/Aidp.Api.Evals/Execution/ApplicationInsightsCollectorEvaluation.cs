using Aidp.Api.Evals.Contracts;
using Aidp.Api.Models;
using Aidp.Api.Services;
using Aidp.Api.Validation;
using Azure;
using Microsoft.Extensions.Options;

namespace Aidp.Api.Evals.Execution;

internal static class ApplicationInsightsCollectorEvaluation
{
    private const string AppInsightsId = "/subscriptions/00000000-0000-4000-8000-000000000000/resourceGroups/rg-aidp-example-dev/providers/Microsoft.Insights/components/appi-inventory-example-dev";
    private static readonly ApplicationResourceDescriptor Resource = new(
        "app-inventory-example-dev", "dev",
        "/subscriptions/00000000-0000-4000-8000-000000000000/resourceGroups/rg-aidp-example-dev/providers/Microsoft.Web/sites/app-inventory-example-dev",
        AppInsightsId, "appi-inventory-example-dev", "44444444-4444-4444-4444-444444444444", ApplicationResourceType.AppService,
        new HashSet<TrustedEvidenceSource> { TrustedEvidenceSource.AzureMonitorMetrics, TrustedEvidenceSource.ApplicationInsights });

    internal static async Task<IReadOnlyList<EvaluationCaseResult>> RunAsync()
    {
        var results = new List<EvaluationCaseResult>();
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var options = Options.Create(new AzureMonitorOptions { TimeoutSeconds = 15 });
        var catalog = Catalog();

        results.Add(Result("appinsights-catalog-association-001",
            catalog.TryResolve(Resource.ApplicationName, out var resolved) &&
            resolved.ApplicationInsightsResourceId == AppInsightsId &&
            resolved.AllowedEvidenceSources.Contains(TrustedEvidenceSource.ApplicationInsights),
            "The allowlisted application must resolve its trusted Application Insights association."));
        results.Add(Result("appinsights-unknown-app-001", !catalog.TryResolve("unknown-app", out _),
            "Unknown applications must not resolve an Application Insights target."));

        var requestProperties = typeof(CreateLiveApplicationHealthAnalysisRequest).GetProperties()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        results.Add(Result("appinsights-public-query-boundary-001",
            !requestProperties.Overlaps(["Kql", "Query", "Table", "WorkspaceId", "ApplicationInsightsResourceId"]),
            "The public request must not expose KQL, tables, workspaces, or resource IDs."));

        var basic = ApplicationInsightsHealthCollector.Profiles[ApplicationInsightsQueryProfile.ApplicationBasicHealth];
        results.Add(Result("appinsights-fixed-profile-001", basic.Select(item => item.QueryId).SequenceEqual(new[]
        {
            "appinsights-requests-summary-v1", "appinsights-dependencies-summary-v1",
            "appinsights-failed-requests-v1", "appinsights-failed-dependencies-v1"
        }), "The basic profile must expand to the exact versioned trusted query IDs."));
        results.Add(Result("appinsights-query-injection-001",
            ApplicationInsightsHealthCollector.Profiles.Values.SelectMany(value => value).All(item =>
                !item.Query.Contains("union *", StringComparison.OrdinalIgnoreCase) &&
                !item.Query.Contains("search *", StringComparison.OrdinalIgnoreCase) &&
                !item.Query.Contains("customDimensions", StringComparison.OrdinalIgnoreCase) &&
                item.Query.StartsWith("union isfuzzy=true", StringComparison.Ordinal) &&
                (item.Query.Contains("(AppRequests |", StringComparison.Ordinal) ||
                    item.Query.Contains("(AppDependencies |", StringComparison.Ordinal)) &&
                item.Query.Contains("datatable(", StringComparison.Ordinal)),
            "Only fixed AppRequests and AppDependencies queries may be used."));
        results.Add(Result("appinsights-dedicated-association-001",
            resolved.ApplicationInsightsComponentName == "appi-inventory-example-dev" &&
            resolved.ApplicationInsightsResourceId == AppInsightsId &&
            resolved.LogAnalyticsWorkspaceId == "44444444-4444-4444-4444-444444444444",
            "The workload must resolve its dedicated Application Insights component."));
        results.Add(Result("appinsights-trusted-workspace-001",
            resolved.LogAnalyticsWorkspaceId == "44444444-4444-4444-4444-444444444444",
            "Application Insights queries must use the catalog's trusted workspace association."));

        var noDataCollector = new ApplicationInsightsHealthCollector(new AggregateZeroClient(), options,
            new FixedTimeProvider(now));
        var noRequestData = await noDataCollector.CollectAsync(new(Resource,
            ApplicationInsightsQueryProfile.ApplicationRequests, AzureMetricTimeWindow.Last30Minutes), CancellationToken.None);
        results.Add(Result("appinsights-no-data-001",
            noRequestData.Metadata.Status == ApplicationInsightsCollectionStatus.NoData &&
            noRequestData.Summaries.All(item => item.Status == ApplicationInsightsCollectionStatus.NoData) &&
            noRequestData.Summaries.Single(item => item.Signal == ApplicationInsightsSignal.RequestCount).Value == 0,
            "Zero AppRequests must produce explicit NoData without discarding the zero count."));
        var noDependencyData = await noDataCollector.CollectAsync(new(Resource,
            ApplicationInsightsQueryProfile.ApplicationDependencies, AzureMetricTimeWindow.Last30Minutes), CancellationToken.None);
        results.Add(Result("appinsights-dependency-no-data-001",
            noDependencyData.Metadata.Status == ApplicationInsightsCollectionStatus.NoData &&
            noDependencyData.Summaries.All(item => item.Status == ApplicationInsightsCollectionStatus.NoData) &&
            noDependencyData.Summaries.Single(item => item.Signal == ApplicationInsightsSignal.DependencyCount).Value == 0,
            "Zero AppDependencies must produce explicit NoData without discarding the zero count."));

        var queryFailureCollector = new ApplicationInsightsHealthCollector(new QueryFailureClient(), options,
            new FixedTimeProvider(now));
        var queryFailure = await queryFailureCollector.CollectAsync(new(Resource,
            ApplicationInsightsQueryProfile.ApplicationRequests, AzureMetricTimeWindow.Last30Minutes), CancellationToken.None);
        results.Add(Result("appinsights-query-failure-001",
            queryFailure.Metadata.Status == ApplicationInsightsCollectionStatus.QueryFailed &&
            queryFailure.Summaries.Count == 0,
            "A real Application Insights query exception must remain QueryFailed."));

        var unauthorized = new ApplicationInsightsCollectionResult(
            new("applicationInsights", ApplicationInsightsQueryProfile.ApplicationBasicHealth,
                AzureMetricTimeWindow.Last30Minutes, ApplicationInsightsCollectionStatus.Unauthorized, 4, 0, 1), []);
        var analysis = new CountingHealthAnalysisService();
        var live = new LiveApplicationHealthAnalysisService(catalog, new FixedMetricsCollector(),
            new FixedInsightsCollector(unauthorized), analysis, new FixedTimeProvider(now));
        var unavailable = await live.AnalyzeAsync(new(Resource.ApplicationName,
            "Assess trusted telemetry without treating collector failure as healthy.",
            AnalysisProfile: LiveHealthAnalysisProfile.AppServiceWithApplicationInsights), CancellationToken.None);
        results.Add(Result("appinsights-unauthorized-blocks-analysis-001",
            unavailable.ApplicationInsightsCollection?.Status == ApplicationInsightsCollectionStatus.Unauthorized &&
            unavailable.Analysis is null && analysis.Calls == 0,
            "Authorization failure must block AI analysis and cannot become a healthy conclusion."));

        var sanitized = ApplicationHealthEvidenceSanitizer.Sanitize(TrustedRequest());
        var references = ApplicationHealthAnalysisValidator.GetReferences(sanitized);
        results.Add(Result("appinsights-trusted-provenance-001",
            references.TryGetValue("applicationinsights-001", out var provenance) &&
            provenance == HealthEvidenceProvenance.TrustedApplicationInsights,
            "Collector IDs must resolve to trusted Application Insights provenance."));
        results.Add(Result("appinsights-invented-id-001",
            ApplicationHealthAnalysisValidator.GetSemanticRule(Output(HealthConclusionType.Finding,
                new(HealthEvidenceProvenance.TrustedApplicationInsights, "Invented dependency failure.", "applicationinsights-999")), sanitized)
                == "invalidEvidenceReference",
            "Model-created Application Insights evidence IDs must be rejected."));
        results.Add(Result("appinsights-cross-provenance-001",
            ApplicationHealthAnalysisValidator.GetSemanticRule(Output(HealthConclusionType.Finding,
                new(HealthEvidenceProvenance.TrustedAzureMonitorMetric, "Wrong collector provenance.", "applicationinsights-001")), sanitized)
                == "invalidEvidenceProvenance",
            "Application Insights evidence cannot pretend to be an Azure Monitor metric."));

        var directEvidence = new ApplicationHealthEvidence(HealthEvidenceProvenance.TrustedApplicationInsights,
            "The controlled aggregate reports dependency failures.", "applicationinsights-001");
        results.Add(Result("appinsights-likely-cause-boundary-001",
            ApplicationHealthAnalysisValidator.GetSemanticRule(Output(HealthConclusionType.LikelyCause, directEvidence), sanitized) is null &&
            ApplicationHealthAnalysisValidator.GetSemanticRule(Output(HealthConclusionType.ConfirmedCause, directEvidence), sanitized)
                == "confirmedCauseInsufficientEvidence",
            "Dependency telemetry may support a likely cause but cannot automatically prove a confirmed cause."));

        var summaryProperties = typeof(ApplicationInsightsSummary).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        results.Add(Result("appinsights-data-minimization-001",
            !summaryProperties.Overlaps(["Headers", "Cookies", "UserId", "RequestBody", "CustomDimensions", "ExceptionMessage", "Url", "QueryText"]) &&
            basic.All(item => !item.Query.Contains("Url", StringComparison.OrdinalIgnoreCase) &&
                !item.Query.Contains("User", StringComparison.OrdinalIgnoreCase) &&
                !item.Query.Contains("Exception", StringComparison.OrdinalIgnoreCase)),
            "Trusted evidence must expose only bounded aggregates and safe failure labels."));

        return results;
    }

    private static ConfigurationApplicationResourceCatalog Catalog() => new(Options.Create(new AzureMonitorOptions
    {
        Applications = [new()
        {
            ApplicationName = Resource.ApplicationName, Environment = "dev", ResourceId = Resource.ResourceId,
            ApplicationInsightsResourceId = AppInsightsId, ApplicationInsightsComponentName = "appi-inventory-example-dev",
            LogAnalyticsWorkspaceId = "44444444-4444-4444-4444-444444444444"
        }]
    }));

    private static CreateApplicationHealthAnalysisRequest TrustedRequest() => new(
        "Assess the trusted dependency evidence without inventing a confirmed cause.",
        new(Resource.ApplicationName, "dev", "appservice", null, null, null), null, null,
        [new(ApplicationHealthEvidenceType.TrustedApplicationInsights, null,
            "Application Insights summary: FailedDependencyCount",
            "Application Insights returned the controlled aggregate FailedDependencyCount value 5 Count.",
            [new(HealthMetricName.DependencyFailures, HealthMetricAggregation.Count, 5, "Count", null)])]);

    private static ApplicationHealthModelOutput Output(HealthConclusionType conclusion,
        ApplicationHealthEvidence evidence) => new(
        "The assessment is bounded by trusted telemetry.", OverallHealthAssessment.Degraded,
        [new("Dependency failures were observed.", ApplicationHealthCategory.DependencyFailure, HealthSeverity.Medium,
            conclusion, [evidence], new(HealthConfidenceLevel.High, "The direct telemetry is bounded."),
            "Review dependency telemetry and application logs using read-only checks.")], [],
        ["Compare the dependency signal with request failures."],
        new(HealthConfidenceLevel.Medium, "Telemetry supports a bounded hypothesis."));

    private static EvaluationCaseResult Result(string id, bool passed, string message) => new(
        id, EvaluationAssistantType.ApplicationInsightsCollection,
        passed ? EvaluationOutcome.Passed : EvaluationOutcome.Failed, 0,
        [new("collector-safety", EvaluationAssertionKind.Exact, passed, message, EvaluationSeverity.Critical, true)],
        new("DeterministicCollectorCheck", null, null, null, null, null, null), null, null, null, []);

    private sealed class AggregateZeroClient : IApplicationInsightsQueryClient
    {
        public Task<RawApplicationInsightsQueryResult> QueryAsync(string resourceId,
            ApplicationInsightsQueryDefinition definition, DateTimeOffset start, DateTimeOffset end,
            CancellationToken cancellationToken) => Task.FromResult(new RawApplicationInsightsQueryResult(definition.QueryId,
            [definition.Kind switch
            {
                ApplicationInsightsQueryKind.RequestSummary => new Dictionary<string, object?>
                {
                    ["RequestCount"] = 0L, ["FailedRequestCount"] = 0L,
                    ["AverageRequestDurationMs"] = null, ["P95RequestDurationMs"] = null
                },
                ApplicationInsightsQueryKind.DependencySummary => new Dictionary<string, object?>
                {
                    ["DependencyCount"] = 0L, ["FailedDependencyCount"] = 0L,
                    ["AverageDependencyDurationMs"] = null
                },
                _ => new Dictionary<string, object?>()
            }]));
    }

    private sealed class QueryFailureClient : IApplicationInsightsQueryClient
    {
        public Task<RawApplicationInsightsQueryResult> QueryAsync(string resourceId,
            ApplicationInsightsQueryDefinition definition, DateTimeOffset start, DateTimeOffset end,
            CancellationToken cancellationToken) => throw new Azure.RequestFailedException(400, "synthetic");
    }

    private sealed class FixedMetricsCollector : IAzureMonitorMetricsCollector
    {
        public Task<AzureMetricCollectionResult> CollectAsync(AzureMetricCollectionRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new AzureMetricCollectionResult(
            new(ApplicationEvidenceSource.AzureMonitorMetrics, request.MetricProfile, request.TimeWindow,
                AzureMetricCollectionStatus.Success, 1, 1, 1),
            [new("azuremetric-001", HealthMetricName.Requests, HealthMetricAggregation.Total, 1, "Count", null,
                AzureMetricCollectionStatus.Success)]));
    }

    private sealed class FixedInsightsCollector(ApplicationInsightsCollectionResult result) : IApplicationInsightsHealthCollector
    {
        public Task<ApplicationInsightsCollectionResult> CollectAsync(ApplicationInsightsCollectionRequest request,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class CountingHealthAnalysisService : IApplicationHealthAnalysisService
    {
        internal int Calls { get; private set; }
        public Task<ApplicationHealthAnalysisResponse> AnalyzeAsync(CreateApplicationHealthAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Analysis must not run for unavailable collector evidence.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
