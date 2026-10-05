namespace Aidp.Api.Models;

public sealed record ApplicationRequest(
    Guid RequestId,
    string ResourceType,
    string ApplicationName,
    string Runtime,
    string Environment,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    int? PipelineRunId = null,
    DateTimeOffset? QueuedAt = null,
    string? QueueOutcome = null,
    string? QueueErrorCode = null,
    string? StatusRefreshError = null
);
