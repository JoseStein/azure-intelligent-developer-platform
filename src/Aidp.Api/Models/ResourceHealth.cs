namespace Aidp.Api.Models;

public enum ResourceHealthCollectionStatus { Success, NoData, Unauthorized, Unavailable, Timeout, Failed }
public enum ResourceAvailabilityState { Available, Unavailable, Degraded, Unknown }

public sealed record ResourceHealthCollectionMetadata(
    string Collector, ResourceHealthCollectionStatus Status, ResourceAvailabilityState Availability,
    long DurationMilliseconds);

internal sealed record ResourceHealthCollectionResult(
    ResourceHealthCollectionMetadata Metadata, string? Summary, DateTimeOffset? ObservedAt);
