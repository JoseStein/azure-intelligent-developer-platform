using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aidp.Api.Models;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Aidp.Api.AzureDevOps;

public sealed record QueueResult(string Outcome, string? ErrorCode = null, int? RunId = null);

public sealed class AzureDevOpsQueueClient(HttpClient http, TokenCredential credential, IOptions<AzureDevOpsOptions> options,
    ILogger<AzureDevOpsQueueClient> logger)
{
    public async Task<QueueResult> QueueAsync(ApplicationRequest request)
    {
        var elapsed = Stopwatch.StartNew();
        int? statusCode = null;
        var responseReceived = false;
        QueueResult Complete(QueueResult result, string category)
        {
            logger.LogInformation("Queue attempt: RequestId={RequestId} Outcome={QueueOutcome} ErrorCode={ErrorCode} HttpStatusCode={HttpStatusCode} ElapsedMilliseconds={ElapsedMilliseconds} ResponseReceived={ResponseReceived} FailureCategory={FailureCategory}",
                request.RequestId, result.Outcome, result.ErrorCode, statusCode,
                elapsed.ElapsedMilliseconds, responseReceived, category);
            return result;
        }

        var config = options.Value;
        // Defense in depth: even direct callers cannot queue while disabled.
        if (!config.QueueEnabled) return Complete(new("disabled"), "none");
        if (config.WorkloadPipelineId != 3) return Complete(new("rejected", "pipelineNotAllowed"), "explicitRejection");

        AccessToken token;
        try
        {
            using var authTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            token = await credential.GetTokenAsync(
                new TokenRequestContext(["https://app.vssps.visualstudio.com/.default"]), authTimeout.Token);
        }
        catch (Exception ex) when (ex is Azure.Identity.AuthenticationFailedException or
                                   Azure.Identity.CredentialUnavailableException or OperationCanceledException)
        {
            return Complete(new("rejected", "queueAuthenticationFailed"), "authentication");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"https://dev.azure.com/{Uri.EscapeDataString(config.Organization)}/{Uri.EscapeDataString(config.Project)}/_apis/pipelines/{config.WorkloadPipelineId}/runs?api-version=7.1");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        message.Content = JsonContent.Create(new
        {
            resources = new { repositories = new { self = new { refName = config.WorkloadBranch } } },
            templateParameters = new
            {
                applicationName = request.ApplicationName,
                environment = request.Environment,
                runtime = request.Runtime,
                resourceType = request.ResourceType
            }
        });

        // No retry policy: a lost response may conceal an accepted run.
        try
        {
            using var response = await http.SendAsync(message);
            responseReceived = true;
            statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var category = statusCode switch
                {
                    401 => "authentication",
                    403 => "authorization",
                    408 => "timeout",
                    >= 300 and < 400 => "redirect",
                    >= 400 and < 500 => "explicitRejection",
                    >= 500 => "serverError",
                    _ => "unexpectedStatus"
                };
                return Complete((int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.RequestTimeout
                    ? new("rejected", "queueRejected")
                    : new("unknown", "queueOutcomeUnknown"), category);
            }

            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = body.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt32(out var runId) && runId > 0 &&
                root.TryGetProperty("pipeline", out var pipeline) && pipeline.ValueKind == JsonValueKind.Object &&
                pipeline.TryGetProperty("id", out var pipelineId) && pipelineId.ValueKind == JsonValueKind.Number &&
                pipelineId.TryGetInt32(out var actualPipelineId) && actualPipelineId == config.WorkloadPipelineId)
            {
                return Complete(new("accepted", RunId: runId), "none");
            }
            return Complete(new("unknown", "queueResponseInvalid"), "invalidRunResponse");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            var category = ex switch
            {
                OperationCanceledException => "timeout",
                JsonException => "malformedResponse",
                _ => "transportError"
            };
            return Complete(new("unknown", "queueOutcomeUnknown"), category);
        }
    }
}
