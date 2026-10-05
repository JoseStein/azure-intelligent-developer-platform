using Aidp.Api.Models;
using Aidp.Api.Validation;

namespace Aidp.Api.Services;

public interface IApplicationHealthAnalysisService
{
    Task<ApplicationHealthAnalysisResponse> AnalyzeAsync(CreateApplicationHealthAnalysisRequest request, CancellationToken cancellationToken);
}

public sealed class ApplicationHealthAnalysisService(IPlatformKnowledgeRetriever knowledgeRetriever) : IApplicationHealthAnalysisService
{
    public Task<ApplicationHealthAnalysisResponse> AnalyzeAsync(CreateApplicationHealthAnalysisRequest request, CancellationToken cancellationToken)
    {
        var input = ApplicationHealthEvidenceSanitizer.Sanitize(request);
        var knowledge = knowledgeRetriever.Retrieve(ApplicationHealthKnowledgeQuery.Create(input));
        if (knowledge.Status == KnowledgeRetrievalStatus.KnowledgeConflict)
            throw new AiReviewException("healthAnalysisKnowledgeConflict", 503, "knowledgeRetrieval", "notCalled");
        if (knowledge.Status == KnowledgeRetrievalStatus.StaleKnowledge)
            throw new AiReviewException("healthAnalysisKnowledgeStale", 503, "knowledgeRetrieval", "notCalled");
        if (knowledge.Status == KnowledgeRetrievalStatus.Unavailable)
            throw new AiReviewException("healthAnalysisKnowledgeUnavailable", 503, "knowledgeRetrieval", "notCalled");
        var first = input.Evidence[0];
        return Task.FromResult(new ApplicationHealthAnalysisResponse(
            Guid.NewGuid(), Reference(input),
            "The supplied evidence is available for review, but deterministic development mode does not infer application health.",
            OverallHealthAssessment.Unknown,
            [new ApplicationHealthFinding(
                "Application health cannot be determined from the deterministic development provider.",
                ApplicationHealthCategory.Unknown, HealthSeverity.Informational, HealthConclusionType.Symptom,
                [new ApplicationHealthEvidence(Provenance(first.Type), "A caller supplied this evidence for health analysis.", first.Id)],
                new HealthConfidence(HealthConfidenceLevel.Low, "No external health source or model inference is used in development mode."),
                "Review the supplied evidence and collect direct availability, request, and dependency observations.")],
            ["Direct availability and error-rate evidence may be needed."],
            ["Collect bounded read-only evidence for the affected observation window."],
            new HealthConfidence(HealthConfidenceLevel.Low, "The deterministic provider reports only the limits of the supplied evidence."),
            new(knowledge.Status, knowledge.Chunks.Count, [])));
    }

    internal static SuppliedHealthEvidenceReference Reference(SanitizedApplicationHealthInput input) =>
        new(input.Target.ApplicationName, input.Target.Environment, input.WindowStart, input.WindowEnd, input.Evidence.Count, input.RedactionsApplied);

    private static HealthEvidenceProvenance Provenance(ApplicationHealthEvidenceType type) => type switch
    {
        ApplicationHealthEvidenceType.AzureMetric => HealthEvidenceProvenance.SuppliedAzureMetric,
        ApplicationHealthEvidenceType.ApplicationInsights => HealthEvidenceProvenance.SuppliedApplicationInsights,
        ApplicationHealthEvidenceType.AppServiceLog => HealthEvidenceProvenance.SuppliedAppServiceLog,
        ApplicationHealthEvidenceType.DeploymentMetadata => HealthEvidenceProvenance.SuppliedDeploymentMetadata,
        ApplicationHealthEvidenceType.ResourceHealth => HealthEvidenceProvenance.SuppliedResourceHealth,
        ApplicationHealthEvidenceType.TrustedAzureMonitorMetric => HealthEvidenceProvenance.TrustedAzureMonitorMetric,
        _ => HealthEvidenceProvenance.SuppliedConfigurationMetadata
    };
}
