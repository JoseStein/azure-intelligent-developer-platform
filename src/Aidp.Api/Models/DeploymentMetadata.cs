namespace Aidp.Api.Models;

public enum DeploymentMetadataCollectionStatus { Success, NoData, Unauthorized, Unavailable, Timeout, Failed }
public sealed record DeploymentMetadataCollectionMetadata(string Collector, DeploymentMetadataCollectionStatus Status, long DurationMilliseconds);
internal sealed record DeploymentMetadataCollectionResult(DeploymentMetadataCollectionMetadata Metadata, DateTimeOffset? Timestamp, string? Status, string? Source);
