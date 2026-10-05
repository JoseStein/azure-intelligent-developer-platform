using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Aidp.Api.Models;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Aidp.Api.Services;

internal interface IAzureDeploymentMetadataCollector
{
    Task<DeploymentMetadataCollectionResult> CollectAsync(ApplicationResourceDescriptor resource, CancellationToken cancellationToken);
}

internal sealed class AzureDeploymentMetadataCollector(HttpClient client, TokenCredential credential, ILogger<AzureDeploymentMetadataCollector> logger) : IAzureDeploymentMetadataCollector
{
    public async Task<DeploymentMetadataCollectionResult> CollectAsync(ApplicationResourceDescriptor resource, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        try
        {
            var accessToken = await credential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), token);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://management.azure.com{resource.ResourceId}/deployments?api-version=2022-03-01");
            request.Headers.Authorization = new("Bearer", accessToken.Token);
            using var response = await client.SendAsync(request, token);
            timer.Stop();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Result(DeploymentMetadataCollectionStatus.Unauthorized, timer.ElapsedMilliseconds);
            if (!response.IsSuccessStatusCode) return Result(DeploymentMetadataCollectionStatus.Failed, timer.ElapsedMilliseconds);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var item = json.RootElement.TryGetProperty("value", out var values) && values.GetArrayLength() > 0 ? values[0] : default;
            if (item.ValueKind == JsonValueKind.Undefined) return Result(DeploymentMetadataCollectionStatus.NoData, timer.ElapsedMilliseconds);
            var properties = item.TryGetProperty("properties", out var p) ? p : item;
            DateTimeOffset? timestamp = null;
            foreach (var name in new[] { "receivedTime", "startTime", "endTime" })
                if (properties.TryGetProperty(name, out var value) && value.TryGetDateTimeOffset(out var parsed)) { timestamp = parsed; break; }
            var status = Safe(properties, "status", 40);
            var source = Safe(properties, "type", 80) ?? Safe(properties, "deployer", 80);
            return new(new("azureDeploymentMetadata", timestamp is null ? DeploymentMetadataCollectionStatus.NoData : DeploymentMetadataCollectionStatus.Success, timer.ElapsedMilliseconds), timestamp, status, source);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { logger.LogWarning("Deployment metadata collection timed out after {ElapsedMilliseconds} ms.", timer.ElapsedMilliseconds); return Result(DeploymentMetadataCollectionStatus.Timeout, timer.ElapsedMilliseconds); }
        catch (Azure.Identity.AuthenticationFailedException) { logger.LogWarning("Deployment metadata credential acquisition failed."); return Result(DeploymentMetadataCollectionStatus.Unavailable, timer.ElapsedMilliseconds); }
        catch (HttpRequestException) { logger.LogWarning("Deployment metadata HTTP request failed."); return Result(DeploymentMetadataCollectionStatus.Unavailable, timer.ElapsedMilliseconds); }
        catch (JsonException) { logger.LogWarning("Deployment metadata response parsing failed."); return Result(DeploymentMetadataCollectionStatus.Failed, timer.ElapsedMilliseconds); }
    }
    private static DeploymentMetadataCollectionResult Result(DeploymentMetadataCollectionStatus status, long elapsed) => new(new("azureDeploymentMetadata", status, elapsed), null, null, null);
    private static string? Safe(JsonElement value, string property, int max) => value.TryGetProperty(property, out var item) ? item.GetString()?.Replace("\r", " ").Replace("\n", " ") is { } text ? text[..Math.Min(max, text.Length)] : null : null;
}
