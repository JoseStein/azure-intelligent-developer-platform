using System.Text.Json.Serialization;
using Aidp.Api.Models;

namespace Aidp.Api.Evals.Contracts;

public enum EvaluationAssistantType { InfrastructureReview, DeploymentTroubleshooting, ApplicationHealth, AzureMonitorCollection, ApplicationInsightsCollection }
public enum EvaluationExecutionMode { OfflineDeterministic, LiveFoundry }
public enum EvaluationSeverity { Informational, Low, Medium, High, Critical }
public enum EvaluationAssertionKind { Exact, OneOf, NotAllowed, Required, Forbidden, MaximumAllowed }
public enum EvaluationOutcome { Passed, Failed, Skipped, Inconclusive }
public enum ExpectedDisposition { Success, ValidationProblem, ControlledProviderFailure, ControlledSemanticRejection, ControlledKnowledgeFailure }
public enum CitationRequirement { Optional, Required, Forbidden }
public enum EvaluationClaimType { ObservedFact, Classification, Assessment, LikelyCause, ConfirmedCause, PlatformFact, Recommendation, RemediationClaim, AzureStateClaim }
public enum ClaimSupportRequirement { SuppliedEvidence, TrustedPlatformKnowledge, SuppliedOrKnowledge, InferenceAllowed, Forbidden }
public enum AdversarialInjectionLocation { UserInput, ErrorLog, HealthEvidence, KnowledgeDocument, ProviderOutput }
public enum AdversarialAttackType { UserInstructionOverride, LogInstructionInjection, HealthEvidenceInjection, KnowledgeDocumentInjection, FakeCitationInjection, PromptDisclosure, CredentialExfiltration, ProvenanceManipulation, FakeAzureState, FakeToolClaim, FakeRemediationClaim, SupportBoundaryOverride, ConfidenceManipulation }
public enum ProviderCallExpectation { ExactlyOnce, Blocked }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(InfrastructureReviewEvaluationInput), "infrastructureReview")]
[JsonDerivedType(typeof(TroubleshootingEvaluationInput), "deploymentTroubleshooting")]
[JsonDerivedType(typeof(HealthEvaluationInput), "applicationHealth")]
public abstract record EvaluationInput;

public sealed record InfrastructureReviewEvaluationInput(CreateAiInfrastructureReviewRequest Request) : EvaluationInput;
public sealed record TroubleshootingEvaluationInput(CreateDeploymentTroubleshootingRequest Request) : EvaluationInput;
public sealed record HealthEvaluationInput(CreateApplicationHealthAnalysisRequest Request) : EvaluationInput;

public sealed record ExpectedClassification(string? Exact, HashSet<string> OneOf, HashSet<string> NotAllowed);
public sealed record ExpectedAssessment(string? Exact, HashSet<string> OneOf, HashSet<string> NotAllowed);
public sealed record ExpectedConfidence(string? Exact, HashSet<string> OneOf, string? MaximumAllowed, bool MustIncludeRationale);
public sealed record ExpectedEvidenceBehavior(
    HashSet<string> RequiredProvenanceTypes, HashSet<string> ForbiddenProvenanceTypes,
    HashSet<string> RequiredSourceReferences, HashSet<string> ForbiddenSourceReferences,
    bool RequireAtLeastOneSuppliedEvidence);
public sealed record ExpectedCitationBehavior(
    CitationRequirement Requirement, HashSet<string> RequiredDocumentIds,
    bool AllowAnyRetrievedTrustedChunk, bool ForbidNonRetrievedChunks);
public sealed record ExpectedSafetyBehavior(
    bool MustNotClaimRemediation, bool MustNotInventAzureState, bool MustNotInventIdentity,
    bool MustNotUpgradeCapability, bool MustNotClaimAzureQuery, bool MustNotEchoCredentials,
    bool MustReturnUnknown, bool MustRejectUnsafeOutput);
public sealed record ExpectedKnowledgeBehavior(
    KnowledgeRetrievalStatus ExpectedStatus, HashSet<string> RequiredDocumentIds,
    HashSet<KnowledgeCategory> RequiredCategories, bool MustBlockProviderCall);

public sealed record ExpectedEvaluationResult(
    ExpectedDisposition Disposition, ExpectedClassification? Classification, ExpectedAssessment? Assessment,
    ExpectedConfidence? Confidence, ExpectedEvidenceBehavior Evidence, ExpectedCitationBehavior Citations,
    ExpectedSafetyBehavior Safety, ExpectedKnowledgeBehavior Knowledge,
    HashSet<string> ExpectedSemanticRules, HashSet<string> ExpectedSupportedComponents,
    HashSet<string> ExpectedRecommendationOnlyComponents,
    IReadOnlyList<EvaluationClaimExpectation>? ClaimExpectations = null);

public sealed record EvaluationClaimExpectation(
    string ClaimId, EvaluationClaimType ClaimType, ClaimSupportRequirement SupportRequirement,
    HashSet<string> RequiredEvidenceReferences, HashSet<string> AllowedEvidenceReferences,
    HashSet<string> ForbiddenEvidenceReferences, CitationRequirement CitationRequirement,
    string? MaximumConfidence, bool ForbiddenIfUnsupported, bool InferenceAllowed,
    bool ConfirmedCauseForbidden);

public sealed record EvaluationClaimResult(
    string ClaimId, EvaluationClaimType ClaimType, bool Grounded, bool Unsupported,
    bool CitationRequired, bool CitationSatisfied, bool InferredAsFact,
    bool ConfirmedCauseViolation, bool InventedAzureState, bool FalseConfidence);

public sealed record EvaluationCase(
    int SchemaVersion, string CaseId, EvaluationAssistantType AssistantType, string Title, string Description,
    EvaluationInput Input, ExpectedEvaluationResult Expected, string ProviderFixture, string? ProviderOutputJson,
    string KnowledgeMode, IReadOnlyList<string> Tags, EvaluationSeverity Severity, bool LiveEligible,
    AdversarialExpectation? Adversarial = null);

public sealed record AdversarialExpectation(
    AdversarialInjectionLocation InjectionLocation, AdversarialAttackType AttackType,
    string ExpectedSafeBehavior, HashSet<string> ForbiddenOutputs,
    ExpectedDisposition RequiredDisposition, ProviderCallExpectation ProviderCallExpectation,
    string KnowledgeBehavior, string? ExpectedSemanticRule);

public sealed record EvaluationAssertionResult(
    string AssertionId, EvaluationAssertionKind Kind, bool Passed, string SafeMessage,
    EvaluationSeverity Severity, bool HardGate);

public sealed record SafeResponseMetadata(
    string? Disposition, string? Classification, string? Assessment, string? Confidence,
    string? ErrorCode, string? FailureStage, string? SemanticRule);

public sealed record SafeTokenUsage(int? InputTokens, int? OutputTokens);

public sealed record EvaluationCaseResult(
    string CaseId, EvaluationAssistantType AssistantType, EvaluationOutcome Outcome,
    long LatencyMilliseconds, IReadOnlyList<EvaluationAssertionResult> Assertions,
    SafeResponseMetadata ResponseMetadata, AiKnowledgeGrounding? KnowledgeGrounding,
    string? ProviderStatus, SafeTokenUsage? TokenUsage,
    IReadOnlyList<EvaluationClaimResult> Claims);

public sealed record EvaluationMetrics(
    double TotalPassRate, bool HardGatesPassed, double ClassificationAccuracy,
    double InsufficientEvidenceCorrectness, double CitationValidityRate,
    double ProvenanceCorrectness, double SchemaAdherence,
    int FalseHighConfidenceCount, int EvaluatedConfidenceCases, double FalseHighConfidenceRate,
    int UnsupportedCapabilityViolations, int RemediationSafetyViolations,
    int AzureQueryClaimViolations,
    double StructuredGroundedClaimRate, int UnsupportedStructuredClaimCount,
    double UnsupportedStructuredClaimRate, double CitationPrecision, double CitationRecall,
    int InferredAsFactViolationCount, int ConfirmedCauseViolationCount,
    int InventedAzureStateClaimCount,
    int AdversarialCaseCount, int PromptInjectionCases, int PromptInjectionBlocked,
    double PromptInjectionBlockRate, int CredentialExfiltrationViolations,
    int ProvenanceManipulationViolations, int FakeToolClaimViolations,
    int FakeRemediationClaimViolations, int SupportBoundaryViolations,
    int PromptDisclosureViolations);

public sealed record EvaluationRun(
    Guid RunId, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, EvaluationExecutionMode Mode,
    string? ModelDeployment, string RepositoryRevision, string KnowledgeRevision,
    int CaseCount, int Passed, int Failed, int Skipped,
    EvaluationMetrics Metrics, IReadOnlyList<EvaluationCaseResult> Cases);
