using Aidp.Api.Models;
using Aidp.Api.Validation;

namespace Aidp.Api.Services;

public sealed class DeploymentTroubleshootingService(IPlatformKnowledgeRetriever knowledgeRetriever) : IDeploymentTroubleshootingService
{
    public Task<DeploymentTroubleshootingResponse> TroubleshootAsync(
        CreateDeploymentTroubleshootingRequest request, CancellationToken cancellationToken)
    {
        var input = DeploymentTroubleshootingSanitizer.Sanitize(request);
        var knowledge = knowledgeRetriever.Retrieve(DeploymentTroubleshootingKnowledgeQuery.Create(input));
        if (knowledge.Status == KnowledgeRetrievalStatus.KnowledgeConflict)
            throw new AiReviewException("troubleshootingKnowledgeConflict", 503, "knowledgeRetrieval", "notCalled");
        if (knowledge.Status == KnowledgeRetrievalStatus.StaleKnowledge)
            throw new AiReviewException("troubleshootingKnowledgeStale", 503, "knowledgeRetrieval", "notCalled");
        if (knowledge.Status == KnowledgeRetrievalStatus.Unavailable)
            throw new AiReviewException("troubleshootingKnowledgeUnavailable", 503, "knowledgeRetrieval", "notCalled");
        return Task.FromResult(new DeploymentTroubleshootingResponse(
            Guid.NewGuid(),
            new TroubleshootingInputReference(
                input.PipelineName, input.RunId, input.FailedStage, input.FailedJob, input.FailedTask,
                input.TerraformCommand is not null, input.AzureContext is not null, input.RedactionsApplied),
            "The supplied deployment evidence is not sufficient to establish a root cause.",
            DeploymentFailureCategory.Unknown,
            [new DeploymentTroubleshootingFinding(
                "The failure requires additional deployment evidence.",
                [new TroubleshootingEvidence(
                    TroubleshootingEvidenceType.SuppliedPipelineEvidence,
                    "A deployment error was supplied for analysis.", "errorText")],
                new TroubleshootingConfidence(TroubleshootingConfidenceLevel.Low,
                    "The deterministic development provider does not infer a root cause."),
                "Review the failed task output and collect the relevant read-only platform state.")],
            ["The exact platform response and affected resource state are not available."],
            ["Collect the complete sanitized failure output from the failed task."],
            new TroubleshootingConfidence(TroubleshootingConfidenceLevel.Low,
                "Only caller-supplied metadata is available in deterministic development mode."),
            new(knowledge.Status, knowledge.Chunks.Count, [])));
    }
}
