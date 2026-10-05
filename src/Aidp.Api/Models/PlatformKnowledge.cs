namespace Aidp.Api.Models;

public enum KnowledgeCategory { PlatformCapability, ArchitectureStandard, SecurityStandard, NetworkingStandard, TerraformStandard, PipelineStandard, TroubleshootingRunbook, HealthRunbook, CostStandard, NamingStandard, OperationalProcedure, KnownLimitation }
public enum KnowledgeStatus { Draft, Active, Deprecated, Retired }
public enum KnowledgeAuthority { PlatformCapability, PlatformStandard, OperationalGuidance, KnownLimitation }
public enum KnowledgeFactKind { Capability, Requirement, Prohibition, Recommendation, Limitation }
public enum KnowledgeRetrievalStatus { Success, NoRelevantKnowledge, StaleKnowledge, KnowledgeConflict, Unavailable }
public enum KnowledgeCitationType { TrustedPlatformKnowledge, ModelInference }
public enum KnowledgeCommandSafety { ReadOnly, StateChanging, Destructive }

public sealed record TrustedFact(string FactId, KnowledgeFactKind Kind, string Statement);

public sealed record KnowledgeDocument(
    string DocumentId, string Title, KnowledgeCategory Category, string Version, KnowledgeStatus Status,
    IReadOnlyList<string> EnvironmentScopes, IReadOnlyList<string> WorkloadScopes, DateOnly LastUpdated,
    string Owner, KnowledgeAuthority Authority, IReadOnlyList<string> Tags, DateOnly ReviewAfter,
    string? Supersedes, string SourcePath, string RepositoryRevision, string ContentHash,
    IReadOnlyList<TrustedFact> TrustedFacts, string Content);

public sealed record KnowledgeChunk(
    string ChunkId, string DocumentId, string DocumentVersion, string Title, KnowledgeCategory Category,
    KnowledgeAuthority Authority, string SectionPath, IReadOnlyList<string> EnvironmentScopes,
    IReadOnlyList<string> WorkloadScopes, IReadOnlyList<string> Tags, DateOnly LastUpdated, DateOnly ReviewAfter, string SourcePath,
    string RepositoryRevision, string ContentHash, string Content, IReadOnlyList<TrustedFact> TrustedFacts);

public sealed record KnowledgeDocumentReference(
    string DocumentId, string Version, string Title, string SourcePath, DateOnly LastUpdated, DateOnly ReviewAfter);

public sealed record KnowledgeConflict(string FactId, IReadOnlyList<KnowledgeDocumentReference> Documents);

public sealed record KnowledgeRetrievalResult(
    KnowledgeRetrievalStatus Status, IReadOnlyList<KnowledgeChunk> Chunks,
    IReadOnlyList<KnowledgeConflict> Conflicts, IReadOnlyList<KnowledgeDocumentReference> StaleDocuments);

public sealed record KnowledgeQuery(
    string Environment, string? WorkloadType, IReadOnlySet<KnowledgeCategory> Categories,
    IReadOnlyList<string> ControlledTerms, int MaximumChunks = 5);

public sealed record KnowledgeCitation(KnowledgeCitationType Type, string Statement, string? SourceReference);

public sealed record KnowledgeCatalogStatus(
    bool Available, int DocumentCount, int ActiveDocumentCount, int ChunkCount,
    int StaleDocumentCount, int ConflictCount, string? FailureCode);
