using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Aidp.Api.AzureDevOps;

public sealed record StatusRefresh(string? Status = null, string? Error = null);

public sealed class AzureDevOpsStatusClient(HttpClient http, TokenCredential credential, IOptions<AzureDevOpsOptions> options)
{
    public async Task<StatusRefresh> RefreshAsync(int runId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var config = options.Value;
            if (runId <= 0 || config.WorkloadPipelineId != 3) return new(Error: "invalidRun");
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://app.vssps.visualstudio.com/.default"]), timeout.Token);
            var url = $"https://dev.azure.com/{Uri.EscapeDataString(config.Organization)}/{Uri.EscapeDataString(config.Project)}/_apis/build/builds/{runId}";
            async Task<JsonDocument?> Read(string path)
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, path);
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                using var response = await http.SendAsync(message, timeout.Token);
                if (!response.IsSuccessStatusCode) return null;
                return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            }
            using var build = await Read(url + "?api-version=7.1");
            if (build is null) return new(Error: "statusLookupFailed");
            var root = build.RootElement;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt32(out var actualId) || actualId != runId ||
                !root.TryGetProperty("definition", out var definition) || !definition.TryGetProperty("id", out var defId) ||
                !defId.TryGetInt32(out var actualDefinition) || actualDefinition != 3) return new(Error: "invalidRunResponse");
            var state = Text(root, "status");
            if (state == "completed") return Text(root, "result") switch
            {
                "succeeded" => new("succeeded"), "failed" or "partiallySucceeded" => new("failed"),
                "canceled" => new("canceled"), _ => new(Error: "unrecognizedRunState")
            };
            if (state is "notStarted" or "postponed") return new("queued");
            if (state != "inProgress") return new(Error: "unrecognizedRunState");
            using var timeline = await Read(url + "/timeline?api-version=7.1");
            if (timeline is null) return new(Error: "statusLookupFailed");
            return MapTimeline(timeline.RootElement);
        }
        catch (OperationCanceledException) { return new(Error: "statusLookupTimeout"); }
        catch (Exception ex) when (ex is Azure.Identity.AuthenticationFailedException or Azure.Identity.CredentialUnavailableException)
        { return new(Error: "statusAuthenticationFailed"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException)
        { return new(Error: "statusLookupFailed"); }
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    public static StatusRefresh MapTimeline(JsonElement timeline)
    {
        if (!timeline.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            return new(Error: "invalidTimeline");
        var stages = records.EnumerateArray().Where(r => Text(r, "type") == "Stage").ToArray();
        JsonElement? Stage(string name) => stages.Where(r => Text(r, "identifier") == name)
            .Select(r => (JsonElement?)r).SingleOrDefault();
        var apply = Stage("TerraformApply");
        var plan = Stage("TerraformPlan");
        var validate = Stage("Validate");
        if (apply is { } a && Text(a, "state") == "inProgress") return new("deploying");
        if (apply is { } pending && Text(pending, "state") == "pending" && plan is { } p &&
            Text(p, "state") == "completed" && Text(p, "result") == "succeeded")
            return new("awaitingApproval"); // Inferred waiting phase for this pipeline, not approval API confirmation.
        if ((plan is { } activePlan && Text(activePlan, "state") == "inProgress") ||
            (validate is { } activeValidate && Text(activeValidate, "state") == "inProgress")) return new("planning");
        return new(Error: "unrecognizedStageState");
    }
}
