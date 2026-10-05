using Aidp.Api.Evals.Contracts;
using Aidp.Api.Models;
using Aidp.Api.Services;
using Aidp.Api.Validation;
using Azure;
using Microsoft.Extensions.Options;

namespace Aidp.Api.Evals.Execution;

internal static class AzureMonitorCollectorEvaluation
{
    private static readonly ApplicationResourceDescriptor Resource = new(
        "app-inventory-example-dev", "dev",
        "/subscriptions/00000000-0000-4000-8000-000000000000/resourceGroups/rg-aidp-example-dev/providers/Microsoft.Web/sites/app-inventory-example-dev",
        null, null, null,
        ApplicationResourceType.AppService, new HashSet<TrustedEvidenceSource> { TrustedEvidenceSource.AzureMonitorMetrics });

    internal static async Task<IReadOnlyList<EvaluationCaseResult>> RunAsync()
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var results = new List<EvaluationCaseResult>();
        var options = Options.Create(new AzureMonitorOptions { TimeoutSeconds = 15 });

        var catalog = new ConfigurationApplicationResourceCatalog(Options.Create(new AzureMonitorOptions
        {
            Applications = [new() { ApplicationName = Resource.ApplicationName, Environment = "dev", ResourceId = Resource.ResourceId }]
        }));
        results.Add(Result("collector-allowlist-resolution-001", catalog.TryResolve(Resource.ApplicationName, out var resolved) && resolved.ResourceId == Resource.ResourceId,
            "The allowlisted application must resolve to the trusted descriptor."));
        results.Add(Result("collector-unknown-app-001", !catalog.TryResolve("unknown-app", out _),
            "Unknown applications must not resolve."));

        var publicProperties = typeof(CreateLiveApplicationHealthAnalysisRequest).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        results.Add(Result("collector-public-boundary-001", !publicProperties.Contains("ResourceId") && !publicProperties.Contains("SubscriptionId") &&
            !publicProperties.Contains("MetricName"), "The public request must not expose resource IDs, subscriptions, or metric names."));

        var basic = AzureMonitorMetricsCollector.Profiles[AzureMetricProfile.AppServiceBasicHealth];
        results.Add(Result("collector-profile-expansion-001",
            basic.Select(metric => (metric.AzureName, metric.Aggregation)).SequenceEqual(new[]
            {
                ("Requests", HealthMetricAggregation.Total), ("Http5xx", HealthMetricAggregation.Total),
                ("Http4xx", HealthMetricAggregation.Total), ("HttpResponseTime", HealthMetricAggregation.Average)
            }), "The basic profile must expand to the exact approved metric set."));
        results.Add(Result("collector-metric-injection-001", !Enum.GetNames<AzureMetricProfile>().Contains("Custom") &&
            AzureMonitorMetricsCollector.Profiles.Values.SelectMany(value => value).All(metric => metric.AzureName is
                "Requests" or "Http5xx" or "Http4xx" or "HttpResponseTime" or "CpuTime" or "AverageMemoryWorkingSet"),
            "Unsupported metrics cannot enter a controlled profile."));

        var noDataCollector = new AzureMonitorMetricsCollector(new FakeClient([]), options, new FixedTimeProvider(now));
        var noData = await noDataCollector.CollectAsync(new(Resource, AzureMetricProfile.AppServiceBasicHealth,
            AzureMetricTimeWindow.Last30Minutes), CancellationToken.None);
        results.Add(Result("collector-no-data-001", noData.Metadata.Status == AzureMetricCollectionStatus.NoData &&
            noData.Metrics.All(metric => metric.Value is null), "No data must remain null rather than becoming zero."));

        var unauthorizedCollector = new AzureMonitorMetricsCollector(new ThrowingClient(new RequestFailedException(403, "synthetic")), options,
            new FixedTimeProvider(now));
        var unauthorized = await unauthorizedCollector.CollectAsync(new(Resource, AzureMetricProfile.AppServiceBasicHealth,
            AzureMetricTimeWindow.Last30Minutes), CancellationToken.None);
        var analysis = new CountingHealthAnalysisService();
        var live = new LiveApplicationHealthAnalysisService(catalog, new FixedCollector(unauthorized),
            new UnusedApplicationInsightsCollector(), analysis, new FixedTimeProvider(now));
        var unavailableResponse = await live.AnalyzeAsync(new(Resource.ApplicationName,
            "Assess the application without treating unavailable metrics as healthy."), CancellationToken.None);
        results.Add(Result("collector-authorization-001", unauthorized.Metadata.Status == AzureMetricCollectionStatus.Unauthorized &&
            unauthorized.Metrics.Count == 0 && unavailableResponse.Analysis is null && analysis.Calls == 0,
            "Authorization failure must become controlled unavailable metadata and cannot produce a healthy analysis."));

        var trustedRequest = TrustedRequest("azuremetric-001");
        var sanitized = ApplicationHealthEvidenceSanitizer.Sanitize(trustedRequest);
        var references = ApplicationHealthAnalysisValidator.GetReferences(sanitized);
        results.Add(Result("collector-trusted-provenance-001", references.TryGetValue("azuremetric-001", out var provenance) &&
            provenance == HealthEvidenceProvenance.TrustedAzureMonitorMetric,
            "Collector evidence IDs must resolve to trusted Azure Monitor provenance."));

        var inventedOutput = Output(new(HealthEvidenceProvenance.TrustedAzureMonitorMetric, "Invented metric value.", "azuremetric-999"));
        results.Add(Result("collector-invented-id-001",
            ApplicationHealthAnalysisValidator.GetSemanticRule(inventedOutput, sanitized) == "invalidEvidenceReference",
            "A model-created collector evidence ID must be rejected."));
        var knowledgeOutput = Output(new(HealthEvidenceProvenance.TrustedPlatformKnowledge, "Pretend metric.", "azuremetric-001"));
        results.Add(Result("collector-provenance-confusion-001",
            ApplicationHealthAnalysisValidator.GetSemanticRule(knowledgeOutput, sanitized) == "invalidKnowledgeProvenance",
            "Collector evidence cannot pretend to be trusted platform knowledge."));
        var unknownLow = Output(new(HealthEvidenceProvenance.TrustedAzureMonitorMetric, "No conclusive signal.", "azuremetric-001"))
            with { OverallHealthAssessment = OverallHealthAssessment.Unknown, OverallConfidence = new(HealthConfidenceLevel.Low, "Evidence is incomplete.") };
        var unknownMedium = unknownLow with { OverallConfidence = new(HealthConfidenceLevel.Medium, "Some evidence exists.") };
        var unknownHigh = unknownLow with { OverallConfidence = new(HealthConfidenceLevel.High, "Evidence is strong.") };
        results.Add(Result("health-unknown-low-confidence-001", ApplicationHealthAnalysisValidator.GetSemanticRule(unknownLow, sanitized) is null,
            "Unknown health must use low confidence."));
        results.Add(Result("health-unknown-medium-confidence-001", ApplicationHealthAnalysisValidator.GetSemanticRule(unknownMedium, sanitized) == "unknownConfidenceMismatch",
            "Unknown health with medium confidence must be rejected."));
        results.Add(Result("health-unknown-high-confidence-001", ApplicationHealthAnalysisValidator.GetSemanticRule(unknownHigh, sanitized) == "unknownConfidenceMismatch",
            "Unknown health with high confidence must be rejected."));
        results.Add(Result("health-trusted-attribution-001",
            !ApplicationHealthEvidenceSanitizer.ContainsInventedAzureQueryClaim("The trusted Azure Resource Health evidence reports Available.") &&
            !ApplicationHealthEvidenceSanitizer.ContainsInventedAzureQueryClaim("The collected Azure Monitor evidence shows no elevated CPU.") &&
            !ApplicationHealthEvidenceSanitizer.ContainsInventedAzureQueryClaim("The supplied Application Insights evidence indicates no failed requests."),
            "Trusted Azure evidence attribution must not be classified as an invented query."));
        results.Add(Result("health-invented-query-still-rejected-001",
            ApplicationHealthEvidenceSanitizer.ContainsInventedAzureQueryClaim("I queried Azure and confirmed the application state.") &&
            ApplicationHealthEvidenceSanitizer.ContainsInventedAzureQueryClaim("Azure Resource Health shows Available."),
            "Direct or unqualified Azure query claims must remain rejected."));

        return results;
    }

    private static CreateApplicationHealthAnalysisRequest TrustedRequest(string ignored) => new(
        "Assess the trusted metric without inventing unavailable Azure state.",
        new(Resource.ApplicationName, "dev", "appservice", null, null, null), null, null,
        [new(ApplicationHealthEvidenceType.TrustedAzureMonitorMetric, null, "Azure Monitor metric: Http5xx",
            "Azure Monitor returned a collected Total value for Http5xx in the controlled observation window.",
            [new(HealthMetricName.Http5xx, HealthMetricAggregation.Total, 5, "Count", null)])]);

    private static ApplicationHealthModelOutput Output(ApplicationHealthEvidence evidence) => new(
        "The assessment is bounded by trusted evidence.", OverallHealthAssessment.Degraded,
        [new("A metric signal was observed.", ApplicationHealthCategory.HttpErrors, HealthSeverity.Medium,
            HealthConclusionType.Symptom, [evidence], new(HealthConfidenceLevel.Medium, "Direct evidence is bounded."),
            "Review related read-only evidence.")], [], ["Review the metric window."],
        new(HealthConfidenceLevel.Medium, "The assessment uses only referenced evidence."));

    private static EvaluationCaseResult Result(string id, bool passed, string message) => new(
        id, EvaluationAssistantType.AzureMonitorCollection, passed ? EvaluationOutcome.Passed : EvaluationOutcome.Failed,
        0, [new("collector-safety", EvaluationAssertionKind.Exact, passed, message, EvaluationSeverity.Critical, true)],
        new("DeterministicCollectorCheck", null, null, null, null, null, null), null, null, null, []);

    private sealed class FakeClient(IReadOnlyList<RawAzureMetric> metrics) : IAzureMetricsQueryClient
    {
        public Task<IReadOnlyList<RawAzureMetric>> QueryAsync(string resourceId, IReadOnlyList<string> metricNames,
            DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) => Task.FromResult(metrics);
    }

    private sealed class ThrowingClient(Exception exception) : IAzureMetricsQueryClient
    {
        public Task<IReadOnlyList<RawAzureMetric>> QueryAsync(string resourceId, IReadOnlyList<string> metricNames,
            DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) => Task.FromException<IReadOnlyList<RawAzureMetric>>(exception);
    }

    private sealed class FixedCollector(AzureMetricCollectionResult result) : IAzureMonitorMetricsCollector
    {
        public Task<AzureMetricCollectionResult> CollectAsync(AzureMetricCollectionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class UnusedApplicationInsightsCollector : IApplicationInsightsHealthCollector
    {
        public Task<ApplicationInsightsCollectionResult> CollectAsync(ApplicationInsightsCollectionRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Application Insights collector must not be called.");
    }

    private sealed class CountingHealthAnalysisService : IApplicationHealthAnalysisService
    {
        internal int Calls { get; private set; }
        public Task<ApplicationHealthAnalysisResponse> AnalyzeAsync(CreateApplicationHealthAnalysisRequest request, CancellationToken cancellationToken)
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
