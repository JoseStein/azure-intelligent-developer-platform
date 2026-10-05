using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Aidp.Api.Models;
using Azure.Core;
using Azure;

namespace Aidp.Api.Services;

internal interface IAzureResourceHealthCollector
{
    Task<ResourceHealthCollectionResult> CollectAsync(ApplicationResourceDescriptor resource, CancellationToken cancellationToken);
}

internal sealed class AzureResourceHealthCollector(HttpClient client, TokenCredential credential)
    : IAzureResourceHealthCollector
{
    public async Task<ResourceHealthCollectionResult> CollectAsync(ApplicationResourceDescriptor resource, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://management.azure.com{resource.ResourceId}/providers/Microsoft.ResourceHealth/availabilityStatuses/current?api-version=2025-05-01");
            request.Headers.Authorization = new("Bearer", token.Token);
            using var response = await client.SendAsync(request, cancellationToken);
            timer.Stop();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Result(ResourceHealthCollectionStatus.Unauthorized, ResourceAvailabilityState.Unknown, timer.ElapsedMilliseconds);
            if (!response.IsSuccessStatusCode)
                return Result(ResourceHealthCollectionStatus.Failed, ResourceAvailabilityState.Unknown, timer.ElapsedMilliseconds);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var properties = document.RootElement.GetProperty("properties");
            var state = Normalize(properties.TryGetProperty("availabilityState", out var availability) ? availability.GetString() : null);
            var summary = properties.TryGetProperty("summary", out var text) ? Sanitize(text.GetString()) : null;
            var observed = properties.TryGetProperty("reportedTime", out var time) && time.TryGetDateTimeOffset(out var parsed) ? parsed : (DateTimeOffset?)null;
            return new(new("azureResourceHealth", ResourceHealthCollectionStatus.Success, state, timer.ElapsedMilliseconds), summary, observed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Result(ResourceHealthCollectionStatus.Timeout, ResourceAvailabilityState.Unknown, timer.ElapsedMilliseconds); }
        catch (RequestFailedException) { return Result(ResourceHealthCollectionStatus.Unavailable, ResourceAvailabilityState.Unknown, timer.ElapsedMilliseconds); }
        catch (HttpRequestException) { return Result(ResourceHealthCollectionStatus.Unavailable, ResourceAvailabilityState.Unknown, timer.ElapsedMilliseconds); }
        catch (JsonException) { return Result(ResourceHealthCollectionStatus.Failed, ResourceAvailabilityState.Unknown, timer.ElapsedMilliseconds); }
    }

    private static ResourceHealthCollectionResult Result(ResourceHealthCollectionStatus status, ResourceAvailabilityState state, long elapsed) =>
        new(new("azureResourceHealth", status, state, elapsed), null, null);
    private static ResourceAvailabilityState Normalize(string? value) => value?.ToLowerInvariant() switch
    {
        "available" => ResourceAvailabilityState.Available,
        "unavailable" => ResourceAvailabilityState.Unavailable,
        "degraded" => ResourceAvailabilityState.Degraded,
        _ => ResourceAvailabilityState.Unknown
    };
    private static string? Sanitize(string? value) => value is null ? null : value.Replace("\r", " ").Replace("\n", " ")[..Math.Min(value.Length, 300)];
}
