using System.Collections.Concurrent;
using Aidp.Api.Models;

namespace Aidp.Api.Storage;

// Temporary development storage: requests are lost on restart and are not shared across API instances.
public sealed class InMemoryApplicationRequestStore
{
    private readonly ConcurrentDictionary<Guid, ApplicationRequest> _requests = new();

    public ApplicationRequest Create(CreateApplicationRequest request)
    {
        while (true)
        {
            var created = new ApplicationRequest(
                Guid.NewGuid(),
                request.ResourceType,
                request.ApplicationName,
                request.Runtime,
                request.Environment,
                request.Description,
                "validated",
                DateTimeOffset.UtcNow);

            if (_requests.TryAdd(created.RequestId, created))
            {
                return created;
            }
        }
    }

    public bool TryBeginQueue(Guid requestId, out ApplicationRequest? claimed)
    {
        claimed = null;
        if (!_requests.TryGetValue(requestId, out var current) ||
            current.Status != "validated" || current.QueueOutcome is not null) return false;
        var updated = current with { QueueOutcome = "attempting" };
        if (!_requests.TryUpdate(requestId, updated, current)) return false;
        claimed = updated;
        return true;
    }

    public ApplicationRequest CompleteQueue(ApplicationRequest claimed, AzureDevOps.QueueResult result)
    {
        var updated = claimed with
        {
            Status = result.Outcome == "accepted" ? "queued" : result.Outcome == "rejected" ? "failed" : "validated",
            PipelineRunId = result.RunId,
            QueuedAt = result.Outcome == "accepted" ? DateTimeOffset.UtcNow : null,
            QueueOutcome = result.Outcome,
            QueueErrorCode = result.ErrorCode
        };
        if (!_requests.TryUpdate(claimed.RequestId, updated, claimed))
            throw new InvalidOperationException("Queue attempt has already been completed.");
        return updated;
    }

    public ApplicationRequest ApplyRefresh(ApplicationRequest snapshot, AzureDevOps.StatusRefresh refresh)
    {
        var updated = snapshot with { Status = refresh.Status ?? snapshot.Status, StatusRefreshError = refresh.Error };
        if (_requests.TryUpdate(snapshot.RequestId, updated, snapshot)) return updated;
        return _requests[snapshot.RequestId]; // Another update won; do not overwrite it with stale data.
    }

    public bool TryGet(Guid requestId, out ApplicationRequest? request) =>
        _requests.TryGetValue(requestId, out request);
}
