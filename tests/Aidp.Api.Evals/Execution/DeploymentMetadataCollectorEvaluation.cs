using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aidp.Api.Evals.Contracts;
using Aidp.Api.Models;
using Aidp.Api.Services;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aidp.Api.Evals.Execution;

internal static class DeploymentMetadataCollectorEvaluation
{
    private static readonly ApplicationResourceDescriptor Resource = new("app", "dev", "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Web/sites/app", null, null, null, ApplicationResourceType.AppService, new HashSet<TrustedEvidenceSource>());

    internal static async Task<IReadOnlyList<EvaluationCaseResult>> RunAsync()
    {
        var results = new List<EvaluationCaseResult>();
        async Task<DeploymentMetadataCollectionResult> Run(HttpMessageHandler handler, TokenCredential credential, CancellationToken cancellationToken = default)
        {
            using var client = new HttpClient(handler);
            return await new AzureDeploymentMetadataCollector(client, credential, NullLogger<AzureDeploymentMetadataCollector>.Instance).CollectAsync(Resource, cancellationToken);
        }
        var success = await Run(new JsonHandler("{\"value\":[{\"properties\":{\"receivedTime\":\"2026-09-30T12:00:00Z\",\"status\":\"success\",\"type\":\"zip\"}}]}"), new FixedCredential());
        results.Add(Result("deployment-success-001", success.Metadata.Status == DeploymentMetadataCollectionStatus.Success && success.Timestamp is not null && success.Status == "success" && success.Source == "zip"));
        var empty = await Run(new JsonHandler("{\"value\":[]}"), new FixedCredential());
        results.Add(Result("deployment-no-data-001", empty.Metadata.Status == DeploymentMetadataCollectionStatus.NoData));
        var auth = await Run(new JsonHandler("{}"), new ThrowingCredential(new Azure.Identity.AuthenticationFailedException("secret-not-logged")));
        results.Add(Result("deployment-auth-failure-001", auth.Metadata.Status == DeploymentMetadataCollectionStatus.Unavailable));
        var authTimeout = await Run(new JsonHandler("{}"), new CancelingCredential());
        results.Add(Result("deployment-auth-timeout-001", authTimeout.Metadata.Status == DeploymentMetadataCollectionStatus.Timeout));
        var httpTimeout = await Run(new CancelingHandler(), new FixedCredential());
        results.Add(Result("deployment-http-timeout-001", httpTimeout.Metadata.Status == DeploymentMetadataCollectionStatus.Timeout));
        using var caller = new CancellationTokenSource(); caller.Cancel();
        var callerCanceled = false;
        try { await Run(new CallerCancelHandler(), new FixedCredential(), caller.Token); } catch (OperationCanceledException) { callerCanceled = true; }
        results.Add(Result("deployment-caller-cancellation-001", callerCanceled));
        var invalid = await Run(new RawHandler("not-json"), new FixedCredential());
        results.Add(Result("deployment-invalid-json-001", invalid.Metadata.Status == DeploymentMetadataCollectionStatus.Failed));
        results.Add(Result("deployment-sanitized-diagnostics-001", !success.Metadata.Collector.Contains("secret", StringComparison.OrdinalIgnoreCase)));
        return results;
    }

    private static EvaluationCaseResult Result(string id, bool passed) => new(id, EvaluationAssistantType.ApplicationHealth, passed ? EvaluationOutcome.Passed : EvaluationOutcome.Failed, 0, [], new(null, null, null, null, null, null, null), null, null, null, []);
    private sealed class FixedCredential : TokenCredential { public override AccessToken GetToken(TokenRequestContext r, CancellationToken c) => new("token", DateTimeOffset.UtcNow.AddMinutes(5)); public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext r, CancellationToken c) => ValueTask.FromResult(GetToken(r, c)); }
    private sealed class ThrowingCredential(Exception error) : TokenCredential { public override AccessToken GetToken(TokenRequestContext r, CancellationToken c) => throw error; public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext r, CancellationToken c) => ValueTask.FromException<AccessToken>(error); }
    private sealed class CancelingCredential : TokenCredential { public override AccessToken GetToken(TokenRequestContext r, CancellationToken c) => throw new OperationCanceledException(c); public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext r, CancellationToken c) => ValueTask.FromException<AccessToken>(new OperationCanceledException(c)); }
    private sealed class JsonHandler(string json) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(JsonDocument.Parse(json).RootElement) }); }
    private sealed class RawHandler(string content) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }); }
    private sealed class CancelingHandler : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromException<HttpResponseMessage>(new OperationCanceledException()); }
    private sealed class CallerCancelHandler : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromException<HttpResponseMessage>(new OperationCanceledException(c)); }
}
