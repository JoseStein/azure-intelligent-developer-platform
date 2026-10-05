using Aidp.Api.AzureDevOps;
using Aidp.Api.Storage;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

static class StatusChecks
{
    public static async Task Run()
    {
        foreach (var (stage, state, result, expected) in new[] {
            ("Validate", "inProgress", "", "planning"), ("TerraformPlan", "inProgress", "", "planning"),
            ("TerraformApply", "inProgress", "", "deploying"), ("TerraformApply", "pending", "", "awaitingApproval") })
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { records = new[] {
                new { type = "Stage", identifier = "TerraformPlan", state = stage == "TerraformPlan" ? state : "completed", result = "succeeded" },
                new { type = "Stage", identifier = stage == "TerraformPlan" ? "Validate" : stage, state, result }
            }}));
            if (AzureDevOpsStatusClient.MapTimeline(doc.RootElement).Status != expected) throw new Exception("Stage mapping failed.");
        }
        foreach (var (state, result, expected) in new[] { ("notStarted", "", "queued"), ("completed", "succeeded", "succeeded"),
            ("completed", "failed", "failed"), ("completed", "partiallySucceeded", "failed"), ("completed", "canceled", "canceled") })
        {
            var handler = new StatusHandler(JsonSerializer.Serialize(new { id = 123, definition = new { id = 3 }, status = state, result }));
            using var http = new HttpClient(handler);
            var client = new AzureDevOpsStatusClient(http, new FakeCredential(), Options.Create(new AzureDevOpsOptions {
                Organization = "example-org", Project = "aidp-public-platform", WorkloadPipelineId = 3 }));
            if ((await client.RefreshAsync(123)).Status != expected || handler.Calls != 1) throw new Exception("Run mapping failed.");
        }
        foreach (var body in new[] { "not json", "{}", "{\"id\":123,\"definition\":{\"id\":2}}" })
        {
            using var http = new HttpClient(new StatusHandler(body));
            var client = new AzureDevOpsStatusClient(http, new FakeCredential(), Options.Create(new AzureDevOpsOptions {
                Organization = "example-org", Project = "aidp-public-platform", WorkloadPipelineId = 3 }));
            var result = await client.RefreshAsync(123);
            if (result.Status is not null || result.Error is null) throw new Exception("Invalid status response accepted.");
        }
        var store = new InMemoryApplicationRequestStore();
        var initial = store.Create(new("appservice", "inventory-api", "dotnet10", "dev", null));
        var newer = store.ApplyRefresh(initial, new("planning"));
        if (store.ApplyRefresh(initial, new("queued")) != newer) throw new Exception("Stale refresh overwrote status.");
        var failed = store.ApplyRefresh(newer, new(Error: "statusLookupFailed"));
        if (failed.Status != "planning" || failed.StatusRefreshError != "statusLookupFailed") throw new Exception("Refresh failure lost status.");
        Console.WriteLine("PASS: lifecycle mapping, stale refresh protection, and failure preservation");
    }
}
sealed class StatusHandler(string body = "unavailable") : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (request.Method != HttpMethod.Get || request.RequestUri!.AbsoluteUri != "https://dev.azure.com/example-org/aidp-public-platform/_apis/build/builds/123?api-version=7.1" ||
            request.Headers.Authorization?.Parameter != "test-credential") throw new Exception("Unexpected status request.");
        return Task.FromResult(new HttpResponseMessage(body == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
