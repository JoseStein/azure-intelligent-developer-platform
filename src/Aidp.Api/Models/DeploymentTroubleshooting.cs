namespace Aidp.Api.Models;

public enum DeploymentFailureCategory
{
    Authentication,
    Authorization,
    Networking,
    TerraformConfiguration,
    TerraformProviderSchema,
    AzureQuotaCapacity,
    AzurePolicyGovernance,
    DeploymentConfiguration,
    ApplicationDeployment,
    Unknown
}

public enum TroubleshootingEvidenceType
{
    SuppliedPipelineEvidence,
    PlatformKnownFact,
    TrustedPlatformKnowledge,
    ModelInference
}

public enum TroubleshootingConfidenceLevel { Low, Medium, High }

public sealed record CreateDeploymentTroubleshootingRequest(
    string PipelineName,
    long RunId,
    string? FailedStage,
    string? FailedJob,
    string? FailedTask,
    string ErrorText,
    string? TerraformCommand,
    AzureDeploymentContext? AzureContext);

public sealed record AzureDeploymentContext(
    string? Service,
    string? ResourceType,
    string? ResourceName,
    string? ResourceGroup,
    string? Region);

public sealed record TroubleshootingInputReference(
    string PipelineName,
    long RunId,
    string? FailedStage,
    string? FailedJob,
    string? FailedTask,
    bool TerraformCommandSupplied,
    bool AzureContextSupplied,
    bool RedactionsApplied);

public sealed record TroubleshootingEvidence(
    TroubleshootingEvidenceType Type,
    string Statement,
    string? SourceReference);

public sealed record TroubleshootingConfidence(
    TroubleshootingConfidenceLevel Level,
    string Rationale);

public sealed record DeploymentTroubleshootingFinding(
    string Finding,
    IReadOnlyList<TroubleshootingEvidence> Evidence,
    TroubleshootingConfidence Confidence,
    string RecommendedInvestigation);

public sealed record DeploymentTroubleshootingResponse(
    Guid TroubleshootingId,
    TroubleshootingInputReference SuppliedEvidence,
    string Summary,
    DeploymentFailureCategory FailureCategory,
    IReadOnlyList<DeploymentTroubleshootingFinding> Findings,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> RecommendedNextSteps,
    TroubleshootingConfidence OverallConfidence,
    AiKnowledgeGrounding KnowledgeGrounding);

internal sealed record SanitizedDeploymentTroubleshootingInput(
    string PipelineName,
    long RunId,
    string? FailedStage,
    string? FailedJob,
    string? FailedTask,
    string ErrorText,
    string? TerraformCommand,
    AzureDeploymentContext? AzureContext,
    bool RedactionsApplied);

internal sealed record DeploymentTroubleshootingModelOutput(
    string Summary,
    DeploymentFailureCategory FailureCategory,
    IReadOnlyList<DeploymentTroubleshootingFinding> Findings,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> RecommendedNextSteps,
    TroubleshootingConfidence OverallConfidence);
