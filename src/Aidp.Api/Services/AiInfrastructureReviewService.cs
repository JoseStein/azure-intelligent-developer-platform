using Aidp.Api.Models;

namespace Aidp.Api.Services;

public interface IAiInfrastructureReviewService
{
    Task<AiInfrastructureReviewResponse> ReviewAsync(CreateAiInfrastructureReviewRequest request, CancellationToken cancellationToken);
}

public sealed class AiInfrastructureReviewService(IPlatformKnowledgeRetriever knowledgeRetriever) : IAiInfrastructureReviewService
{
    public Task<AiInfrastructureReviewResponse> ReviewAsync(CreateAiInfrastructureReviewRequest request, CancellationToken cancellationToken)
    {
        var knowledge = knowledgeRetriever.Retrieve(AiInfrastructureKnowledgeQuery.Create(request));
        if (knowledge.Status == KnowledgeRetrievalStatus.KnowledgeConflict)
            throw new AiReviewException("aiReviewKnowledgeConflict", 503, "knowledgeRetrieval", "notCalled");
        if (knowledge.Status == KnowledgeRetrievalStatus.StaleKnowledge)
            throw new AiReviewException("aiReviewKnowledgeStale", 503, "knowledgeRetrieval", "notCalled");
        if (knowledge.Status == KnowledgeRetrievalStatus.Unavailable)
            throw new AiReviewException("aiReviewKnowledgeUnavailable", 503, "knowledgeRetrieval", "notCalled");
        var supplied = request.CurrentRequest;
        AiEvidence Supplied(string statement) => new(AiEvidenceType.SuppliedRequest, statement);
        AiEvidence Capability(string statement) => new(AiEvidenceType.CurrentPlatformCapability, statement, "AIDP provisioning contract");
        AiEvidence Standard(string statement) => new(AiEvidenceType.PlatformStandard, statement, "AIDP platform standards");
        AiEvidence Inference(string statement) => new(AiEvidenceType.ModelInference, statement);

        var architecture = new AiArchitectureRecommendationModelOutput[]
        {
            new("Azure App Service", "Host the .NET inventory API.",
                "Use the currently supported .NET 10 App Service workload.",
                [Capability("The current platform supports App Service with .NET 10 in dev.")]),
            new("Managed Identity", "Authenticate the application to supported Azure services without application secrets.",
                "Prefer Managed Identity over client secrets where a supported service integration requires Azure access.",
                [Standard("AIDP prefers managed identities and scoped RBAC over stored credentials.")]),
            new("Azure Blob Storage", "Store application files.",
                "Evaluate Blob Storage and grant access through Managed Identity and RBAC instead of storage keys.",
                [Inference("The file-storage requirement implies a storage service may be needed.")]),
            new("Application Insights", "Collect application telemetry for diagnosis and operational monitoring.",
                "Define telemetry, alerting, and retention requirements before adding monitoring resources.",
                [Inference("The request does not define application monitoring requirements.")])
        };

        return Task.FromResult(new AiInfrastructureReviewResponse(
            Guid.NewGuid(),
            supplied,
            "App Service fits the current platform request. File storage, authentication, capacity, and public exposure need further design decisions.",
            architecture.Select(item => AiPlatformCapabilityCatalog.CreateRecommendation(item, supplied)).ToArray(),
            [
                new(AiFindingCategory.Security, "Public exposure needs an explicit authentication and authorization design.", AiFindingSeverity.High,
                    [Supplied("The stated intent requests a public API for internal users.")],
                    "Clarify whether public means internet-reachable and require authenticated access for internal users."),
                new(AiFindingCategory.Security, "Storage credentials should not be embedded in application configuration.", AiFindingSeverity.Medium,
                    [Standard("AIDP prefers identity-based access and scoped RBAC.")],
                    "Use Managed Identity and Blob data-plane RBAC if storage becomes a supported part of the workload.")
            ],
            [
                new(AiFindingCategory.Reliability, "Traffic, availability, and monitoring requirements are incomplete.", AiFindingSeverity.Medium,
                    [Supplied("The intent gives a user count but no request rate, availability target, or telemetry requirements.")],
                    "Define peak traffic, availability expectations, Application Insights telemetry, and alerting needs.")
            ],
            [
                new(AiFindingCategory.Cost, "The dev workload may be able to reuse the existing shared App Service Plan.", AiFindingSeverity.Informational,
                    [Capability("The current dev golden path uses the existing development App Service Plan.")],
                    "Validate available capacity and estimate storage, monitoring, and bandwidth costs separately.")
            ],
            [
                new(AiFindingCategory.MissingInformation, "Expected traffic, file volume, retention, and precise access boundaries are unknown.", AiFindingSeverity.Medium,
                    [Supplied("The intent does not specify request rate, file sizes, retention, or client network locations.")],
                    "Collect peak concurrency, file size and retention estimates, and whether clients connect through the public internet.")
            ],
            AiRiskLevel.High,
            new(AiConfidenceLevel.Medium, "App Service matches the supported workload, but storage, traffic, identity, and exposure requirements are incomplete."),
            "This deterministic review separates supplied information, current platform capabilities, platform standards, and recommendations. Blob Storage and Application Insights are recommendations only and are not added to the provisioning request.",
            [
                "Clarify authentication and public-access requirements.",
                "Estimate traffic and file-storage volume.",
                "Review recommendation-only components before extending the supported golden path.",
                "Submit the provisioning request separately after human review."
            ],
            new(knowledge.Status, knowledge.Chunks.Count, [])));
    }
}
