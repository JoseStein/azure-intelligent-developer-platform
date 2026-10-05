namespace Aidp.Api.Models;

public enum AiRiskLevel { Low, Medium, High, Critical }
public enum AiFindingSeverity { Informational, Low, Medium, High, Critical }
public enum AiFindingCategory { Architecture, Security, Reliability, Operations, Cost, PlatformSupport, MissingInformation }
public enum AiConfidenceLevel { Low, Medium, High }
public enum AiEvidenceType { SuppliedRequest, CurrentPlatformCapability, PlatformStandard, TrustedPlatformKnowledge, ModelInference }
public enum PlatformSupportStatus { Supported, RecommendationOnly }

public sealed record CreateAiInfrastructureReviewRequest(
    string Intent,
    ProvisioningRequestSnapshot? CurrentRequest);

public sealed record ProvisioningRequestSnapshot(
    string ResourceType,
    string ApplicationName,
    string Runtime,
    string Environment,
    string? Description);

public sealed record AiEvidence(
    AiEvidenceType Type,
    string Statement,
    string? SourceReference = null);

public sealed record AiReviewFinding(
    AiFindingCategory Category,
    string Finding,
    AiFindingSeverity Severity,
    IReadOnlyList<AiEvidence> Evidence,
    string Recommendation);

public sealed record AiArchitectureRecommendation(
    string Component,
    string Purpose,
    PlatformSupportStatus SupportStatus,
    string Recommendation,
    IReadOnlyList<AiEvidence> Evidence);

public sealed record AiReviewConfidence(
    AiConfidenceLevel Level,
    string Rationale);

public sealed record AiKnowledgeCitation(
    string ChunkId,
    string DocumentId,
    string DocumentVersion,
    string Title,
    string SectionPath);

public sealed record AiKnowledgeGrounding(
    KnowledgeRetrievalStatus Status,
    int RetrievedChunkCount,
    IReadOnlyList<AiKnowledgeCitation> Citations);

public sealed record AiInfrastructureReviewResponse(
    Guid ReviewId,
    ProvisioningRequestSnapshot? SuppliedRequest,
    string Summary,
    IReadOnlyList<AiArchitectureRecommendation> RecommendedArchitecture,
    IReadOnlyList<AiReviewFinding> SecurityFindings,
    IReadOnlyList<AiReviewFinding> ReliabilityFindings,
    IReadOnlyList<AiReviewFinding> CostConsiderations,
    IReadOnlyList<AiReviewFinding> MissingInformation,
    AiRiskLevel RiskLevel,
    AiReviewConfidence Confidence,
    string EvidenceReasoningSummary,
    IReadOnlyList<string> RecommendedNextSteps,
    AiKnowledgeGrounding KnowledgeGrounding);

internal sealed record AiArchitectureRecommendationModelOutput(
    string Component,
    string Purpose,
    string Recommendation,
    IReadOnlyList<AiEvidence> Evidence);

internal sealed record AiInfrastructureReviewModelOutput(
    string Summary,
    IReadOnlyList<AiArchitectureRecommendationModelOutput> RecommendedArchitecture,
    IReadOnlyList<AiReviewFinding> SecurityFindings,
    IReadOnlyList<AiReviewFinding> ReliabilityFindings,
    IReadOnlyList<AiReviewFinding> CostConsiderations,
    IReadOnlyList<AiReviewFinding> MissingInformation,
    AiRiskLevel RiskLevel,
    AiReviewConfidence Confidence,
    string EvidenceReasoningSummary,
    IReadOnlyList<string> RecommendedNextSteps);
