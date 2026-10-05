using Aidp.Api.AzureDevOps;
using Aidp.Api.Models;
using Aidp.Api.Services;
using Aidp.Api.Storage;
using Aidp.Api.Validation;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Aidp.Api"));
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_AIDP_API", root);
const string tenant = "11111111-1111-1111-1111-111111111111";
const string clientId = "22222222-2222-2222-2222-222222222222";
const string issuer = "https://login.microsoftonline.com/" + tenant + "/v2.0";
using var rsa = RSA.Create(2048);
var key = new RsaSecurityKey(rsa) { KeyId = "local-auth-checks" };
using var factory = new WebApplicationFactory<Aidp.Api.Models.ApplicationRequest>().WithWebHostBuilder(builder =>
{
    builder.UseEnvironment("Development");
    builder.ConfigureServices(services =>
    {
        services.PostConfigure<AzureDevOpsOptions>(options => options.QueueEnabled = false);
        services.AddSingleton<TokenCredential>(new FakeCredential());
        services.AddHttpClient<AzureDevOpsStatusClient>().ConfigurePrimaryHttpMessageHandler(() => new StatusHandler());
        services.AddHttpClient<AzureDevOpsQueueClient>().ConfigurePrimaryHttpMessageHandler(
            () => new FakeQueueHandler(_ => throw new Exception("Disabled queue called HTTP.")));
        services.AddSingleton<IAzureMonitorMetricsCollector, FakeAzureMonitorCollector>();
        services.RemoveAll<LogsQueryClient>();
        services.RemoveAll<IApplicationInsightsQueryClient>();
        services.RemoveAll<IApplicationInsightsHealthCollector>();
        services.AddSingleton<IApplicationInsightsHealthCollector, FakeApplicationInsightsCollector>();
    });
    builder.ConfigureServices(services => services.PostConfigureAll<JwtBearerOptions>(options =>
        {
            // Test-host-only signing metadata: production authentication remains intact.
            var metadata = new OpenIdConnectConfiguration { Issuer = issuer };
            metadata.SigningKeys.Add(key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            options.TokenValidationParameters.ValidIssuer = issuer;
        }));
});
using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
{
    BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
});
if (factory.Services.GetRequiredService<IAiInfrastructureReviewService>() is not AiInfrastructureReviewService)
    throw new Exception("Development did not select the deterministic AI review stub.");
if (factory.Services.GetRequiredService<IApplicationHealthAnalysisService>() is not ApplicationHealthAnalysisService)
    throw new Exception("Development did not select the deterministic health-analysis stub.");
Console.WriteLine("PASS: Development selects Stub provider");

string Token(string? scope, string? role, string audience = clientId)
{
    var claims = new List<Claim> { new("tid", tenant), new("ver", "2.0"), new("sub", "lab-user") };
    if (scope is not null) claims.Add(new("scp", scope));
    if (role is not null) claims.Add(new("roles", role));
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        issuer, audience, claims, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
        new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));
}

async Task<HttpResponseMessage> Check(string label, HttpMethod method, string path,
    HttpStatusCode expected, string? token = null, string name = "inventory-api")
{
    using var request = new HttpRequestMessage(method, path);
    if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    if (method == HttpMethod.Post) request.Content = JsonContent.Create(new
    {
        resourceType = "appservice", applicationName = name, runtime = "dotnet10", environment = "dev", description = "Auth check"
    });
    var response = await client.SendAsync(request);
    if (response.StatusCode != expected) throw new Exception($"{label}: expected {expected}, got {response.StatusCode}");
    Console.WriteLine($"PASS: {label}");
    return response;
}

await Check("anonymous POST", HttpMethod.Post, "/api/requests", HttpStatusCode.Unauthorized);
await Check("missing scope", HttpMethod.Post, "/api/requests", HttpStatusCode.Forbidden, Token(null, "AIDP.Provisioner"));
await Check("missing role", HttpMethod.Post, "/api/requests", HttpStatusCode.Forbidden, Token("Requests.Submit", null));
await Check("scope substring rejected", HttpMethod.Post, "/api/requests", HttpStatusCode.Forbidden, Token("Requests.SubmitExtra", "AIDP.Provisioner"));
await Check("wrong audience", HttpMethod.Post, "/api/requests", HttpStatusCode.Unauthorized, Token("Requests.Submit", "AIDP.Provisioner", "another-api"));
var accepted = await Check("scope and role", HttpMethod.Post, "/api/requests", HttpStatusCode.Created, Token("other Requests.Submit", "AIDP.Provisioner"));
var path = accepted.Headers.Location!.ToString();
await Check("anonymous GET", HttpMethod.Get, path, HttpStatusCode.Unauthorized);
await Check("GET requires role", HttpMethod.Get, path, HttpStatusCode.Forbidden, Token("Requests.Submit", null));
await Check("GET role without submit scope", HttpMethod.Get, path, HttpStatusCode.OK, Token(null, "AIDP.Provisioner"));
await Check("reserved aidp name", HttpMethod.Post, "/api/requests", HttpStatusCode.BadRequest, Token("Requests.Submit", "AIDP.Provisioner"), "aidp");
await Check("anonymous health", HttpMethod.Get, "/health", HttpStatusCode.OK);
await Check("storage diagnostic removed", HttpMethod.Get, "/storage-test", HttpStatusCode.NotFound);
await Check("ADO diagnostic removed", HttpMethod.Get, "/ado-auth-test", HttpStatusCode.NotFound);

var reviewBody = new
{
    intent = "I need a public .NET API for inventory that stores files and will be used by about 50 internal users.",
    currentRequest = new
    {
        resourceType = "appservice", applicationName = "inventory-api", runtime = "dotnet10",
        environment = "dev", description = "Inventory API"
    }
};

async Task<HttpResponseMessage> Review(object body, string? token = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/review") { Content = JsonContent.Create(body) };
    if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return await client.SendAsync(request);
}

if ((await Review(reviewBody)).StatusCode != HttpStatusCode.Unauthorized)
    throw new Exception("Anonymous AI review was accepted.");
if ((await Review(reviewBody, Token(null, "AIDP.Provisioner"))).StatusCode != HttpStatusCode.Forbidden ||
    (await Review(reviewBody, Token("Requests.Submit", null))).StatusCode != HttpStatusCode.Forbidden)
    throw new Exception("AI review did not require the existing SubmitRequest policy.");
Console.WriteLine("PASS: AI review requires scope and role");

var authorizedToken = Token("Requests.Submit", "AIDP.Provisioner");
foreach (var invalidBody in new object[]
{
    new { intent = "too short" },
    new { intent = new string('x', 2001) },
    new { intent = reviewBody.intent, currentRequest = new { resourceType = "aks", applicationName = "aidp", runtime = "node", environment = "prod", description = new string('x', 201) } }
})
{
    var invalidReview = await Review(invalidBody, authorizedToken);
    if (invalidReview.StatusCode != HttpStatusCode.BadRequest) throw new Exception("Invalid AI review input was accepted.");
}
using (var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/ai/review"))
{
    malformed.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizedToken);
    malformed.Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
    if ((await client.SendAsync(malformed)).StatusCode != HttpStatusCode.BadRequest)
        throw new Exception("Malformed AI review JSON was accepted.");
}
Console.WriteLine("PASS: AI review validates intent, current request, and malformed JSON");

var reviewResponse = await Review(reviewBody, authorizedToken);
if (reviewResponse.StatusCode != HttpStatusCode.OK) throw new Exception("Valid AI review failed.");
using var reviewJson = JsonDocument.Parse(await reviewResponse.Content.ReadAsStringAsync());
var reviewRoot = reviewJson.RootElement;
var supplied = reviewRoot.GetProperty("suppliedRequest");
if (supplied.GetProperty("resourceType").GetString() != reviewBody.currentRequest.resourceType ||
    supplied.GetProperty("applicationName").GetString() != reviewBody.currentRequest.applicationName ||
    supplied.GetProperty("runtime").GetString() != reviewBody.currentRequest.runtime ||
    supplied.GetProperty("environment").GetString() != reviewBody.currentRequest.environment ||
    supplied.GetProperty("description").GetString() != reviewBody.currentRequest.description)
    throw new Exception("AI review mutated the supplied request.");
if (reviewRoot.GetProperty("riskLevel").GetString() != "high" ||
    reviewRoot.GetProperty("confidence").GetProperty("level").GetString() != "medium" ||
    reviewRoot.GetProperty("securityFindings")[0].GetProperty("severity").GetString() != "high" ||
    reviewRoot.GetProperty("securityFindings")[0].GetProperty("evidence")[0].GetProperty("type").GetString() != "suppliedRequest")
    throw new Exception("AI review enums were not serialized as controlled camel-case values.");
var blob = reviewRoot.GetProperty("recommendedArchitecture").EnumerateArray()
    .Single(item => item.GetProperty("component").GetString() == "Azure Blob Storage");
if (blob.GetProperty("supportStatus").GetString() != "recommendationOnly")
    throw new Exception("Unsupported resource was presented as supported.");

var reviewId = reviewRoot.GetProperty("reviewId").GetGuid();
using var lookup = new HttpRequestMessage(HttpMethod.Get, $"/api/requests/{reviewId}");
lookup.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(null, "AIDP.Provisioner"));
if ((await client.SendAsync(lookup)).StatusCode != HttpStatusCode.NotFound)
    throw new Exception("AI review created an application request record.");
var reviewQueueHandler = new FakeQueueHandler(_ => throw new Exception("AI review called Azure DevOps."));
using (var queueEnabledFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
{
    services.PostConfigure<AzureDevOpsOptions>(options => options.QueueEnabled = true);
    services.AddHttpClient<AzureDevOpsQueueClient>().ConfigurePrimaryHttpMessageHandler(() => reviewQueueHandler);
})))
using (var queueEnabledClient = queueEnabledFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
using (var queueEnabledRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ai/review") { Content = JsonContent.Create(reviewBody) })
{
    queueEnabledRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizedToken);
    if ((await queueEnabledClient.SendAsync(queueEnabledRequest)).StatusCode != HttpStatusCode.OK || reviewQueueHandler.Calls != 0)
        throw new Exception("AI review interacted with the production queue path.");
}
Console.WriteLine("PASS: structured AI review preserves supplied request, marks unsupported resources, creates no application request, and never queues");

var foundryRequest = new CreateAiInfrastructureReviewRequest(reviewBody.intent,
    new("appservice", "inventory-api", "dotnet10", "dev", "Inventory API"));
const string validModelReview = """
{
  "summary":"App Service is suitable for the submitted API.",
  "recommendedArchitecture":[
    {"component":"Azure App Service","purpose":"Host the API.","recommendation":"Use the supported workload.","evidence":[{"type":"currentPlatformCapability","statement":"App Service is supported.","sourceReference":null}]},
    {"component":"Azure Blob Storage","purpose":"Store files.","recommendation":"Evaluate Blob Storage separately.","evidence":[{"type":"suppliedRequest","statement":"File storage was requested.","sourceReference":null}]}
  ],
  "securityFindings":[],"reliabilityFindings":[],"costConsiderations":[],"missingInformation":[],
  "riskLevel":"medium","confidence":{"level":"high","rationale":"The supplied requirements are specific."},
  "evidenceReasoningSummary":"Recommendations are based on supplied data and current capability.",
  "recommendedNextSteps":["Confirm authentication requirements."]
}
""";
var noKnowledgeRetriever = new FakeKnowledgeRetriever(new(
    KnowledgeRetrievalStatus.NoRelevantKnowledge, [], [], []));

async Task<AiReviewException> FoundryFailure(FoundryReviewCompletion completion)
{
    var service = new FoundryAiInfrastructureReviewService(new FakeFoundryReviewClient((_, _) => Task.FromResult(completion)),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
    try { await service.ReviewAsync(foundryRequest, CancellationToken.None); }
    catch (AiReviewException error) { return error; }
    throw new Exception("Invalid Foundry output was accepted.");
}

var foundryService = new FoundryAiInfrastructureReviewService(
    new FakeFoundryReviewClient((_, _) => Task.FromResult(new FoundryReviewCompletion(validModelReview, false, true))),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
var foundryReview = await foundryService.ReviewAsync(foundryRequest, CancellationToken.None);
if (foundryReview.ReviewId == Guid.Empty || foundryReview.SuppliedRequest != foundryRequest.CurrentRequest ||
    foundryReview.RecommendedArchitecture.Single(x => x.Component == "Azure App Service").SupportStatus != PlatformSupportStatus.Supported ||
    foundryReview.RecommendedArchitecture.Single(x => x.Component == "Azure Blob Storage").SupportStatus != PlatformSupportStatus.RecommendationOnly)
    throw new Exception("Foundry response was not placed in the trusted response envelope.");
var intentOnlyService = new FoundryAiInfrastructureReviewService(
    new FakeFoundryReviewClient((_, _) => Task.FromResult(new FoundryReviewCompletion(validModelReview, false, true))),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
var intentOnlyReview = await intentOnlyService.ReviewAsync(new(foundryRequest.Intent, null), CancellationToken.None);
if (intentOnlyReview.RecommendedArchitecture.Single(x => x.Component == "Azure App Service").SupportStatus != PlatformSupportStatus.RecommendationOnly)
    throw new Exception("App Service was marked supported without a canonical validated workload snapshot.");
var malformedFailure = await FoundryFailure(new("{", false, true));
var refusalFailure = await FoundryFailure(new(null, true, true));
var incompleteFailure = await FoundryFailure(new(validModelReview, false, false));
var extractionFailure = await FoundryFailure(new(null, false, true));
if (malformedFailure is not { ErrorCode: "aiReviewInvalidResponse", FailureStage: "jsonDeserialization", ProviderStatus: "completed" } ||
    refusalFailure is not { ErrorCode: "aiReviewRefused", FailureStage: "refusal", ProviderStatus: "refused" } ||
    incompleteFailure is not { ErrorCode: "aiReviewInvalidResponse", FailureStage: "incomplete", ProviderStatus: "incomplete" } ||
    extractionFailure is not { ErrorCode: "aiReviewInvalidResponse", FailureStage: "outputExtraction", ProviderStatus: "completed" })
    throw new Exception("Foundry response failures were not sanitized.");
foreach (var unsupportedComponent in new[]
{
    "Azure Blob Storage",
    "Azure Storage Account",
    "Application Insights",
    "Azure SQL Database",
    "Azure Database for PostgreSQL",
    "Azure Virtual Network",
    "Azure Private Endpoint",
    "Microsoft Entra ID authentication",
    "Azure RBAC assignment",
    "Unrecognized Custom Component",
    "Azure App Service Plan"
})
{
    var recommendationOnlyOutput = validModelReview.Replace("Azure Blob Storage", unsupportedComponent, StringComparison.Ordinal);
    var recommendationService = new FoundryAiInfrastructureReviewService(
        new FakeFoundryReviewClient((_, _) => Task.FromResult(new FoundryReviewCompletion(recommendationOnlyOutput, false, true))),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
    var recommendation = await recommendationService.ReviewAsync(foundryRequest, CancellationToken.None);
    if (recommendation.RecommendedArchitecture.Single(x => x.Component == unsupportedComponent).SupportStatus != PlatformSupportStatus.RecommendationOnly)
        throw new Exception($"{unsupportedComponent} did not default to recommendationOnly.");
}
var modelControlledStatus = validModelReview.Replace(
    "\"component\":\"Azure Blob Storage\"",
    "\"component\":\"Azure Blob Storage\",\"supportStatus\":\"supported\"", StringComparison.Ordinal);
var modelControlledStatusFailure = await FoundryFailure(new(modelControlledStatus, false, true));
if (modelControlledStatusFailure is not { ErrorCode: "aiReviewInvalidResponse", FailureStage: "jsonDeserialization" } ||
    AiReviewJsonSchema.Value.Contains("supportStatus", StringComparison.Ordinal) ||
    !FoundryReviewCompletionClient.SystemInstruction.Contains("trusted platform code assigns support status", StringComparison.Ordinal))
    throw new Exception("The model can still control AIDP support status.");
Console.WriteLine("PASS: trusted capability catalog assigns the App Service-only support boundary");

var knowledgeCatalog = PlatformKnowledgeLoader.Load(
    Path.Combine(AppContext.BaseDirectory, "knowledge"), "test-revision", new DateOnly(2026, 9, 25));
var knowledgeRetriever = new PlatformKnowledgeRetriever(knowledgeCatalog);
var expectedKnowledge = knowledgeRetriever.Retrieve(AiInfrastructureKnowledgeQuery.Create(foundryRequest));
if (expectedKnowledge.Status != KnowledgeRetrievalStatus.Success || expectedKnowledge.Chunks.Count is < 1 or > 5 ||
    !expectedKnowledge.Chunks.Any(chunk => chunk.Category == KnowledgeCategory.PlatformCapability) ||
    !expectedKnowledge.Chunks.Any(chunk => chunk.Category is KnowledgeCategory.ArchitectureStandard or KnowledgeCategory.SecurityStandard))
    throw new Exception("Infrastructure review did not retrieve scoped platform capability and App Service/security knowledge.");
var citedChunk = expectedKnowledge.Chunks[0];
var groundedModelReview = validModelReview.Replace(
    "{\"type\":\"currentPlatformCapability\",\"statement\":\"App Service is supported.\",\"sourceReference\":null}",
    $"{{\"type\":\"trustedPlatformKnowledge\",\"statement\":\"App Service guidance was retrieved.\",\"sourceReference\":\"{citedChunk.ChunkId}\"}}",
    StringComparison.Ordinal);
var groundedCalls = 0;
var groundedService = new FoundryAiInfrastructureReviewService(
    new FakeFoundryReviewClient((input, _) =>
    {
        groundedCalls++;
        if (input.Knowledge.Status != KnowledgeRetrievalStatus.Success || !input.Knowledge.Chunks.Any(x => x.ChunkId == citedChunk.ChunkId))
            throw new Exception("Foundry did not receive the exact trusted retrieval context.");
        return Task.FromResult(new FoundryReviewCompletion(groundedModelReview, false, true));
    }),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
    knowledgeRetriever);
var groundedReview = await groundedService.ReviewAsync(foundryRequest, CancellationToken.None);
var resolvedCitation = groundedReview.KnowledgeGrounding.Citations.Single();
if (groundedCalls != 1 || groundedReview.KnowledgeGrounding.Status != KnowledgeRetrievalStatus.Success ||
    groundedReview.KnowledgeGrounding.RetrievedChunkCount != expectedKnowledge.Chunks.Count ||
    resolvedCitation.ChunkId != citedChunk.ChunkId || resolvedCitation.Title != citedChunk.Title ||
    resolvedCitation.DocumentVersion != citedChunk.DocumentVersion ||
    groundedReview.RecommendedArchitecture.Single(x => x.Component == "Azure Blob Storage").SupportStatus != PlatformSupportStatus.RecommendationOnly)
    throw new Exception("Trusted knowledge grounding metadata or capability isolation failed.");

async Task<AiReviewException> GroundingFailure(string json, KnowledgeRetrievalResult retrieval)
{
    var service = new FoundryAiInfrastructureReviewService(
        new FakeFoundryReviewClient((_, _) => Task.FromResult(new FoundryReviewCompletion(json, false, true))),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
        new FakeKnowledgeRetriever(retrieval));
    try { await service.ReviewAsync(foundryRequest, CancellationToken.None); }
    catch (AiReviewException error) { return error; }
    throw new Exception("Invalid knowledge grounding was accepted.");
}
var inventedCitation = groundedModelReview.Replace(citedChunk.ChunkId, "knowledge:invented@1.0.0#fake:01", StringComparison.Ordinal);
var inventedFailure = await GroundingFailure(inventedCitation, expectedKnowledge);
var otherCatalogChunk = knowledgeCatalog.Chunks.First(chunk => expectedKnowledge.Chunks.All(retrieved => retrieved.ChunkId != chunk.ChunkId));
var limitedRetrieval = expectedKnowledge with { Chunks = [citedChunk] };
var nonRetrievedCitation = groundedModelReview.Replace(citedChunk.ChunkId, otherCatalogChunk.ChunkId, StringComparison.Ordinal);
var nonRetrievedFailure = await GroundingFailure(nonRetrievedCitation, limitedRetrieval);
var inferenceCitation = groundedModelReview.Replace("\"type\":\"trustedPlatformKnowledge\"", "\"type\":\"modelInference\"", StringComparison.Ordinal);
var inferenceFailure = await GroundingFailure(inferenceCitation, expectedKnowledge);
if (inventedFailure.SemanticRule != "invalidKnowledgeCitation" ||
    nonRetrievedFailure.SemanticRule != "invalidKnowledgeCitation" ||
    inferenceFailure.SemanticRule != "invalidKnowledgeProvenance")
    throw new Exception("Knowledge citation failures were not classified safely.");

foreach (var blocked in new[] { KnowledgeRetrievalStatus.KnowledgeConflict, KnowledgeRetrievalStatus.StaleKnowledge })
{
    var blockedRetriever = new FakeKnowledgeRetriever(new(blocked, [], [], []));
    var clientCalls = 0;
    var service = new FoundryAiInfrastructureReviewService(
        new FakeFoundryReviewClient((_, _) => { clientCalls++; return Task.FromResult(new FoundryReviewCompletion(validModelReview, false, true)); }),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
        blockedRetriever);
    try { await service.ReviewAsync(foundryRequest, CancellationToken.None); }
    catch (AiReviewException error) when (error.FailureStage == "knowledgeRetrieval" && clientCalls == 0) { continue; }
    throw new Exception("Unsafe knowledge state did not prevent the Foundry call.");
}
if (foundryReview.KnowledgeGrounding.Status != KnowledgeRetrievalStatus.NoRelevantKnowledge ||
    foundryReview.KnowledgeGrounding.RetrievedChunkCount != 0 || foundryReview.KnowledgeGrounding.Citations.Count != 0)
    throw new Exception("No-relevant-knowledge review did not preserve explicit grounding status.");
if (!AiReviewJsonSchema.Create(expectedKnowledge).Contains(citedChunk.ChunkId, StringComparison.Ordinal) ||
    AiReviewJsonSchema.Create(limitedRetrieval).Contains(otherCatalogChunk.ChunkId, StringComparison.Ordinal))
    throw new Exception("Structured schema did not constrain citations to the exact retrieval result.");
Console.WriteLine("PASS: Phase 5A uses scoped trusted knowledge and validates API-resolved citations without changing capability support");

foreach (var failure in new (Exception Error, string Code)[]
{
    (new AuthenticationFailedException("private credential detail"), "aiReviewAuthenticationFailed"),
    (new HttpRequestException("private provider detail"), "aiReviewProviderUnavailable"),
    (new OperationCanceledException("private timeout detail"), "aiReviewTimeout")
})
{
    var service = new FoundryAiInfrastructureReviewService(new FakeFoundryReviewClient((_, _) => Task.FromException<FoundryReviewCompletion>(failure.Error)),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
    try { await service.ReviewAsync(foundryRequest, CancellationToken.None); }
    catch (AiReviewException error) when (error.ErrorCode == failure.Code && !error.ToString().Contains("private", StringComparison.OrdinalIgnoreCase)) { continue; }
    throw new Exception("Foundry provider failure was not sanitized.");
}
foreach (var providerFailure in new[]
{
    (Status: 401, Code: "aiReviewAuthenticationFailed"),
    (Status: 403, Code: "aiReviewAuthorizationFailed"),
    (Status: 429, Code: "aiReviewRateLimited"),
    (Status: 500, Code: "aiReviewProviderUnavailable")
})
{
    var error = FoundryAiInfrastructureReviewService.ClassifyProviderStatus(providerFailure.Status);
    if (error.ErrorCode != providerFailure.Code || error.StatusCode != 503 ||
        error.FailureStage != "providerResponse" || error.ProviderStatus != "unknown")
        throw new Exception("Foundry HTTP failure was not safely classified.");
}
foreach (var unsupportedKeyword in new[] { "minLength", "maxLength", "minItems", "maxItems" })
    if (AiReviewJsonSchema.Value.Contains(unsupportedKeyword, StringComparison.Ordinal))
        throw new Exception("Foundry schema contains an unsupported strict-schema keyword.");
Console.WriteLine("PASS: Foundry response mapping, semantic validation, refusals, timeouts, and provider failures are sanitized without network calls");

var troubleshootingBody = new
{
    pipelineName = "aidp-workload-provisioning",
    runId = 65,
    errorText = "Terraform failed while reading the remote backend with HTTP 403 AuthorizationFailure."
};

async Task<HttpResponseMessage> Troubleshoot(object body, string? token = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/troubleshoot") { Content = JsonContent.Create(body) };
    if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return await client.SendAsync(request);
}

if ((await Troubleshoot(troubleshootingBody)).StatusCode != HttpStatusCode.Unauthorized ||
    (await Troubleshoot(troubleshootingBody, Token(null, "AIDP.Provisioner"))).StatusCode != HttpStatusCode.Forbidden ||
    (await Troubleshoot(troubleshootingBody, Token("Requests.Submit", null))).StatusCode != HttpStatusCode.Forbidden)
    throw new Exception("Troubleshooting did not require the SubmitRequest policy.");
foreach (var invalid in new object[]
{
    new { pipelineName = "", runId = 65, errorText = troubleshootingBody.errorText },
    new { pipelineName = troubleshootingBody.pipelineName, runId = 0, errorText = troubleshootingBody.errorText },
    new { pipelineName = troubleshootingBody.pipelineName, runId = 65, errorText = "short" },
    new { pipelineName = new string('x', 201), runId = 65, errorText = troubleshootingBody.errorText },
    new { pipelineName = troubleshootingBody.pipelineName, runId = 65, errorText = new string('x', 12001) }
})
    if ((await Troubleshoot(invalid, authorizedToken)).StatusCode != HttpStatusCode.BadRequest)
        throw new Exception("Invalid troubleshooting request was accepted.");
var troubleshootingResponse = await Troubleshoot(troubleshootingBody, authorizedToken);
if (troubleshootingResponse.StatusCode != HttpStatusCode.OK)
    throw new Exception("Troubleshooting rejected optional failed stage/job/task metadata.");
var troubleshootingJson = await troubleshootingResponse.Content.ReadAsStringAsync();
using (var document = JsonDocument.Parse(troubleshootingJson))
{
    var rootElement = document.RootElement;
    if (rootElement.GetProperty("failureCategory").GetString() != "unknown" ||
        rootElement.GetProperty("overallConfidence").GetProperty("level").GetString() != "low" ||
        rootElement.TryGetProperty("errorText", out _) || troubleshootingJson.Contains("AuthorizationFailure"))
        throw new Exception("Troubleshooting response exposed raw evidence or rejected an unknown classification.");
    var troubleshootingId = rootElement.GetProperty("troubleshootingId").GetGuid();
    using var requestLookup = new HttpRequestMessage(HttpMethod.Get, $"/api/requests/{troubleshootingId}");
    requestLookup.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(null, "AIDP.Provisioner"));
    if ((await client.SendAsync(requestLookup)).StatusCode != HttpStatusCode.NotFound)
        throw new Exception("Troubleshooting created an application request.");
}
Console.WriteLine("PASS: troubleshooting authorization, validation, optional execution metadata, unknown classification, and trusted response envelope");

var secretRequest = new CreateDeploymentTroubleshootingRequest(
    "pipeline", 42, null, null, null,
    "Authorization: Bearer private-token\nclient_secret=super-secret\nhttps://example.test/?sv=1&sig=private-signature",
    "terraform plan -var password=hunter2", null);
var sanitized = DeploymentTroubleshootingSanitizer.Sanitize(secretRequest);
if (!sanitized.RedactionsApplied || sanitized.ErrorText.Contains("private-token") ||
    sanitized.ErrorText.Contains("super-secret") || sanitized.ErrorText.Contains("private-signature") ||
    sanitized.TerraformCommand!.Contains("hunter2") ||
    !sanitized.ErrorText.Contains("[REDACTED_CREDENTIAL]"))
    throw new Exception("Troubleshooting sanitizer did not redact credential-like input.");
Console.WriteLine("PASS: troubleshooting sanitizer redacts authorization, secret, SAS, and command credential material");

foreach (var allowedAdvisory in new[]
{
    "Grant the minimum required role.",
    "Verify the pipeline identity's RBAC assignments.",
    "Consider rerunning the pipeline after correcting permissions.",
    "Check whether the role assignment is at the correct scope.",
    "A human operator should validate the effective permissions.",
    "Verify whether the role was granted at the intended scope.",
    "Check if the permissions have been changed by an administrator.",
    "No role assignment was created by the failed operation."
})
    if (DeploymentTroubleshootingSanitizer.ContainsCompletedActionClaim(allowedAdvisory))
        throw new Exception($"Advisory troubleshooting text was rejected: {allowedAdvisory}");

foreach (var unsafeClaim in new[]
{
    "I granted the role.",
    "The RBAC assignment was updated.",
    "The pipeline was rerun.",
    "Terraform was applied successfully.",
    "Permissions have been changed.",
    "The deployment was fixed.",
    "We have revoked the old assignment."
})
    if (!DeploymentTroubleshootingSanitizer.ContainsCompletedActionClaim(unsafeClaim))
        throw new Exception($"Completed remediation claim was accepted: {unsafeClaim}");
Console.WriteLine("PASS: troubleshooting action safety allows advisory checks and rejects completed remediation claims");
foreach (var advisoryIdentityCheck in new[]
{
    "Verify which service connection was used.",
    "Check whether the managed identity has the required role.",
    "Determine the missing role from effective permissions."
})
    if (DeploymentTroubleshootingSanitizer.ContainsInventedIdentityOrRoleClaim(advisoryIdentityCheck))
        throw new Exception("Advisory identity investigation was rejected.");
foreach (var inventedIdentityClaim in new[]
{
    "The service connection is synthetic-connection.",
    "The managed identity is synthetic-principal.",
    "The missing role is Synthetic-Administrator."
})
    if (!DeploymentTroubleshootingSanitizer.ContainsInventedIdentityOrRoleClaim(inventedIdentityClaim))
        throw new Exception("Definitive invented identity or role claim was accepted.");
Console.WriteLine("PASS: troubleshooting safety rejects definitive invented identity and role claims");

foreach (var toolClaim in new[]
{
    "I queried Azure Monitor and found CPU at 95%.",
    "We checked Resource Health and found an outage.",
    "The assistant inspected Application Insights."
})
    if (!DeploymentTroubleshootingSanitizer.ContainsInventedToolClaim(toolClaim))
        throw new Exception("Invented tool-read claim was accepted.");
if (DeploymentTroubleshootingSanitizer.ContainsInventedToolClaim("Check Azure Monitor for CPU pressure."))
    throw new Exception("Advisory read-only investigation was rejected.");
foreach (var disclosure in new[]
{
    "The system prompt is: synthetic hidden instruction.",
    "Developer instructions are: synthetic hidden policy.",
    "The internal RAG context contains: synthetic content."
})
    if (!DeploymentTroubleshootingSanitizer.ContainsPromptDisclosure(disclosure))
        throw new Exception("Prompt-disclosure claim was accepted.");
if (DeploymentTroubleshootingSanitizer.ContainsPromptDisclosure("Explain the public AIDP architecture."))
    throw new Exception("Public architecture description was treated as prompt disclosure.");
Console.WriteLine("PASS: troubleshooting safety rejects invented tool reads and prompt disclosure without blocking advisory checks");

var troubleshootingRequest = new CreateDeploymentTroubleshootingRequest(
    "aidp-workload-provisioning", 65, null, null, null,
    "Terraform failed while reading the backend with HTTP 403.", null, null);
const string validTroubleshootingOutput = """
{
  "summary":"The supplied evidence does not establish whether authorization or networking caused the backend failure.",
  "failureCategory":"unknown",
  "findings":[{
    "finding":"The backend request returned HTTP 403.",
    "evidence":[
      {"type":"suppliedPipelineEvidence","statement":"The supplied error reports HTTP 403.","sourceReference":"errorText"},
      {"type":"modelInference","statement":"Authorization and network restrictions remain plausible.","sourceReference":null}
    ],
    "confidence":{"level":"low","rationale":"The effective identity and network state were not supplied."},
    "recommendedInvestigation":"Verify the effective identity and backend network rules using read-only checks."
  }],
  "missingInformation":["Effective identity and backend network state."],
  "recommendedNextSteps":["Inspect sanitized backend authorization and firewall evidence."],
  "overallConfidence":{"level":"low","rationale":"Multiple causes remain plausible."}
}
""";

async Task<AiReviewException> TroubleshootingFailure(
    string json, bool refused = false, bool complete = true,
    CreateDeploymentTroubleshootingRequest? suppliedRequest = null)
{
    var service = new FoundryDeploymentTroubleshootingService(
        new FakeFoundryTroubleshootingClient((_, _) => Task.FromResult(new FoundryTroubleshootingCompletion(json, refused, complete))),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
    try { await service.TroubleshootAsync(suppliedRequest ?? troubleshootingRequest, CancellationToken.None); }
    catch (AiReviewException error) { return error; }
    throw new Exception("Invalid troubleshooting output was accepted.");
}

var foundryTroubleshooting = new FoundryDeploymentTroubleshootingService(
    new FakeFoundryTroubleshootingClient((_, _) => Task.FromResult(new FoundryTroubleshootingCompletion(validTroubleshootingOutput, false, true))),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
var troubleshootingResult = await foundryTroubleshooting.TroubleshootAsync(troubleshootingRequest, CancellationToken.None);
if (troubleshootingResult.TroubleshootingId == Guid.Empty || troubleshootingResult.FailureCategory != DeploymentFailureCategory.Unknown ||
    troubleshootingResult.SuppliedEvidence.RunId != troubleshootingRequest.RunId)
    throw new Exception("Foundry troubleshooting did not construct the trusted envelope.");

var badPlatformFact = validTroubleshootingOutput.Replace(
    "{\"type\":\"modelInference\",\"statement\":\"Authorization and network restrictions remain plausible.\",\"sourceReference\":null}",
    "{\"type\":\"platformKnownFact\",\"statement\":\"An invented platform fact.\",\"sourceReference\":\"AIDP trusted context\"}");
var badPlatformFactSource = validTroubleshootingOutput.Replace(
    "{\"type\":\"modelInference\",\"statement\":\"Authorization and network restrictions remain plausible.\",\"sourceReference\":null}",
    "{\"type\":\"platformKnownFact\",\"statement\":\"AIDP infrastructure deployments use Terraform.\",\"sourceReference\":\"errorText\"}");
var badSuppliedReference = validTroubleshootingOutput.Replace("\"sourceReference\":\"errorText\"", "\"sourceReference\":\"pipeline error\"");
var failedStageReference = validTroubleshootingOutput.Replace("\"sourceReference\":\"errorText\"", "\"sourceReference\":\"failedStage\"");
var badInferenceReference = validTroubleshootingOutput.Replace("\"sourceReference\":null", "\"sourceReference\":\"errorText\"");
var unknownConfidenceMismatch = validTroubleshootingOutput.Replace(
    "\"overallConfidence\":{\"level\":\"low\"", "\"overallConfidence\":{\"level\":\"medium\"");
var outputLimitViolation = validTroubleshootingOutput.Replace(
    "\"summary\":\"The supplied evidence does not establish whether authorization or networking caused the backend failure.\"",
    "\"summary\":\"\"");
var unsafeCredentialOutput = validTroubleshootingOutput.Replace("Multiple causes remain plausible.", "Authorization: Bearer private-model-token");
var completedActionOutput = validTroubleshootingOutput.Replace("Multiple causes remain plausible.", "I fixed the pipeline configuration.");
if ((await TroubleshootingFailure(badPlatformFact)).SemanticRule != "invalidPlatformKnownFact" ||
    (await TroubleshootingFailure(badPlatformFactSource)).SemanticRule != "invalidPlatformKnownFactSource" ||
    (await TroubleshootingFailure(badSuppliedReference)).SemanticRule != "invalidSuppliedEvidenceReference" ||
    (await TroubleshootingFailure(failedStageReference)).SemanticRule != "invalidSuppliedEvidenceReference" ||
    (await TroubleshootingFailure(badInferenceReference)).SemanticRule != "invalidModelInferenceSource" ||
    (await TroubleshootingFailure(unknownConfidenceMismatch)).SemanticRule != "unknownConfidenceMismatch" ||
    (await TroubleshootingFailure(outputLimitViolation)).SemanticRule != "outputLimitViolation" ||
    (await TroubleshootingFailure(unsafeCredentialOutput)).SemanticRule != "credentialLikeOutput" ||
    (await TroubleshootingFailure(completedActionOutput)) is not { SemanticRule: "unsafeActionClaim", FailureStage: "responseSafety" } ||
    (await TroubleshootingFailure("{", false, true)).FailureStage != "jsonDeserialization" ||
    (await TroubleshootingFailure(validTroubleshootingOutput, true, true)).ErrorCode != "troubleshootingRefused" ||
    (await TroubleshootingFailure(validTroubleshootingOutput, false, false)).FailureStage != "incomplete")
    throw new Exception("Troubleshooting semantic or safety validation failed.");

var troubleshootingRequestWithStage = troubleshootingRequest with { FailedStage = "Terraform Apply" };
var foundryTroubleshootingWithStage = new FoundryDeploymentTroubleshootingService(
    new FakeFoundryTroubleshootingClient((_, _) => Task.FromResult(new FoundryTroubleshootingCompletion(failedStageReference, false, true))),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
await foundryTroubleshootingWithStage.TroubleshootAsync(troubleshootingRequestWithStage, CancellationToken.None);

var minimalTroubleshootingSchema = DeploymentTroubleshootingJsonSchema.Create(
    DeploymentTroubleshootingSanitizer.Sanitize(troubleshootingRequest), new(KnowledgeRetrievalStatus.NoRelevantKnowledge, [], [], []));
if (!minimalTroubleshootingSchema.Contains("\"errorText\"", StringComparison.Ordinal) ||
    minimalTroubleshootingSchema.Contains("\"failedStage\"", StringComparison.Ordinal) ||
    minimalTroubleshootingSchema.Contains("\"azureContext.service\"", StringComparison.Ordinal))
    throw new Exception("Troubleshooting schema did not limit evidence references to supplied fields.");
var stageTroubleshootingSchema = DeploymentTroubleshootingJsonSchema.Create(
    DeploymentTroubleshootingSanitizer.Sanitize(troubleshootingRequestWithStage), new(KnowledgeRetrievalStatus.NoRelevantKnowledge, [], [], []));
if (!stageTroubleshootingSchema.Contains("\"failedStage\"", StringComparison.Ordinal))
    throw new Exception("Troubleshooting schema omitted a supplied optional field reference.");
foreach (var statusFailure in new[]
{
    (Status: 401, Code: "troubleshootingAuthenticationFailed"),
    (Status: 403, Code: "troubleshootingAuthorizationFailed"),
    (Status: 429, Code: "troubleshootingRateLimited"),
    (Status: 500, Code: "troubleshootingProviderUnavailable")
})
{
    var error = FoundryDeploymentTroubleshootingService.ClassifyProviderStatus(statusFailure.Status);
    if (error.ErrorCode != statusFailure.Code || error.StatusCode != 503 || error.FailureStage != "providerResponse")
        throw new Exception("Troubleshooting provider failure was not safely classified.");
}
foreach (var providerException in new (Exception Error, string Code, int Status)[]
{
    (new OperationCanceledException("private timeout detail"), "troubleshootingTimeout", 504),
    (new AuthenticationFailedException("private credential detail"), "troubleshootingAuthenticationFailed", 503),
    (new HttpRequestException("private transport detail"), "troubleshootingProviderUnavailable", 503)
})
{
    var service = new FoundryDeploymentTroubleshootingService(
        new FakeFoundryTroubleshootingClient((_, _) => Task.FromException<FoundryTroubleshootingCompletion>(providerException.Error)),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
    try { await service.TroubleshootAsync(troubleshootingRequest, CancellationToken.None); }
    catch (AiReviewException error) when (error.ErrorCode == providerException.Code && error.StatusCode == providerException.Status &&
        !error.ToString().Contains("private", StringComparison.OrdinalIgnoreCase)) { continue; }
    throw new Exception("Troubleshooting provider exception was not safely mapped.");
}
foreach (var unsupportedKeyword in new[] { "minLength", "maxLength", "minItems", "maxItems", "pattern" })
    if (DeploymentTroubleshootingJsonSchema.Value.Contains(unsupportedKeyword, StringComparison.Ordinal))
        throw new Exception("Troubleshooting schema contains an unsupported strict-schema keyword.");
foreach (var sourceReference in new[]
{
    "pipelineName", "runId", "failedStage", "failedJob", "failedTask", "errorText", "terraformCommand",
    "azureContext.service", "azureContext.resourceType", "azureContext.resourceName", "azureContext.resourceGroup",
    "azureContext.region", "AIDP trusted context"
})
    if (!DeploymentTroubleshootingJsonSchema.Value.Contains($"\"{sourceReference}\"", StringComparison.Ordinal))
        throw new Exception("Troubleshooting schema does not constrain source references to the controlled identifiers.");

var authorizationRequest = troubleshootingRequest with
{
    ErrorText = "Terraform apply returned 403 AuthorizationFailed for Microsoft.Authorization/roleAssignments/write."
};
var sanitizedAuthorization = DeploymentTroubleshootingSanitizer.Sanitize(authorizationRequest);
var troubleshootingQuery = DeploymentTroubleshootingKnowledgeQuery.Create(sanitizedAuthorization);
var troubleshootingKnowledge = knowledgeRetriever.Retrieve(troubleshootingQuery);
if (troubleshootingQuery.Environment != "dev" || troubleshootingQuery.WorkloadType != "appservice" ||
    troubleshootingQuery.MaximumChunks is < 4 or > 6 ||
    !troubleshootingQuery.Categories.Contains(KnowledgeCategory.TroubleshootingRunbook) ||
    !troubleshootingQuery.Categories.Contains(KnowledgeCategory.SecurityStandard) ||
    !troubleshootingQuery.Categories.Contains(KnowledgeCategory.TerraformStandard) ||
    !troubleshootingQuery.Categories.Contains(KnowledgeCategory.PipelineStandard) ||
    !troubleshootingQuery.ControlledTerms.Contains("AuthorizationFailed", StringComparer.OrdinalIgnoreCase) ||
    troubleshootingKnowledge.Status != KnowledgeRetrievalStatus.Success ||
    !troubleshootingKnowledge.Chunks.Any(chunk => chunk.Category == KnowledgeCategory.TroubleshootingRunbook) ||
    !troubleshootingKnowledge.Chunks.Any(chunk => chunk.Category == KnowledgeCategory.TerraformStandard) ||
    !troubleshootingKnowledge.Chunks.Any(chunk => chunk.Category == KnowledgeCategory.PipelineStandard))
    throw new Exception("Troubleshooting knowledge query was not deterministically scoped to relevant runbook, Terraform, pipeline, and security knowledge.");

var troubleshootingChunk = troubleshootingKnowledge.Chunks.First(chunk => chunk.Category == KnowledgeCategory.TroubleshootingRunbook);
var groundedTroubleshootingOutput = validTroubleshootingOutput.Replace(
    "{\"type\":\"modelInference\",\"statement\":\"Authorization and network restrictions remain plausible.\",\"sourceReference\":null}",
    $"{{\"type\":\"trustedPlatformKnowledge\",\"statement\":\"The retrieved runbook distinguishes authorization investigation from remediation.\",\"sourceReference\":\"{troubleshootingChunk.ChunkId}\"}}",
    StringComparison.Ordinal);
var troubleshootingModelCalls = 0;
var groundedTroubleshooting = new FoundryDeploymentTroubleshootingService(
    new FakeFoundryTroubleshootingClient((input, _) =>
    {
        troubleshootingModelCalls++;
        if (!input.Knowledge.Chunks.Any(chunk => chunk.ChunkId == troubleshootingChunk.ChunkId))
            throw new Exception("Foundry troubleshooting did not receive the trusted retrieval result.");
        return Task.FromResult(new FoundryTroubleshootingCompletion(groundedTroubleshootingOutput, false, true));
    }),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
    new FakeKnowledgeRetriever(troubleshootingKnowledge));
var groundedTroubleshootingResult = await groundedTroubleshooting.TroubleshootAsync(authorizationRequest, CancellationToken.None);
var troubleshootingCitation = groundedTroubleshootingResult.KnowledgeGrounding.Citations.Single();
if (troubleshootingModelCalls != 1 || troubleshootingCitation.ChunkId != troubleshootingChunk.ChunkId ||
    troubleshootingCitation.DocumentId != troubleshootingChunk.DocumentId ||
    groundedTroubleshootingResult.KnowledgeGrounding.RetrievedChunkCount != troubleshootingKnowledge.Chunks.Count)
    throw new Exception("Troubleshooting grounding metadata was not resolved from the trusted catalog.");

async Task<AiReviewException> TroubleshootingGroundingFailure(string json, KnowledgeRetrievalResult retrieval,
    CreateDeploymentTroubleshootingRequest? suppliedRequest = null)
{
    var service = new FoundryDeploymentTroubleshootingService(
        new FakeFoundryTroubleshootingClient((_, _) => Task.FromResult(new FoundryTroubleshootingCompletion(json, false, true))),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
        new FakeKnowledgeRetriever(retrieval));
    try { await service.TroubleshootAsync(suppliedRequest ?? authorizationRequest, CancellationToken.None); }
    catch (AiReviewException error) { return error; }
    throw new Exception("Invalid troubleshooting grounding was accepted.");
}

var inventedTroubleshootingCitation = groundedTroubleshootingOutput.Replace(troubleshootingChunk.ChunkId,
    "knowledge:invented@1.0.0#fake:01", StringComparison.Ordinal);
var otherTroubleshootingChunk = knowledgeCatalog.Chunks.First(chunk =>
    troubleshootingKnowledge.Chunks.All(retrieved => retrieved.ChunkId != chunk.ChunkId));
var nonRetrievedTroubleshootingCitation = groundedTroubleshootingOutput.Replace(troubleshootingChunk.ChunkId,
    otherTroubleshootingChunk.ChunkId, StringComparison.Ordinal);
var suppliedAsKnowledge = groundedTroubleshootingOutput.Replace(troubleshootingChunk.ChunkId, "errorText", StringComparison.Ordinal);
var knowledgeAsSupplied = groundedTroubleshootingOutput.Replace("\"type\":\"trustedPlatformKnowledge\"",
    "\"type\":\"suppliedPipelineEvidence\"", StringComparison.Ordinal);
var knowledgeAsInference = groundedTroubleshootingOutput.Replace("\"type\":\"trustedPlatformKnowledge\"",
    "\"type\":\"modelInference\"", StringComparison.Ordinal);
if ((await TroubleshootingGroundingFailure(inventedTroubleshootingCitation, troubleshootingKnowledge)).SemanticRule != "invalidKnowledgeCitation" ||
    (await TroubleshootingGroundingFailure(nonRetrievedTroubleshootingCitation,
        troubleshootingKnowledge with { Chunks = [troubleshootingChunk] })).SemanticRule != "invalidKnowledgeCitation" ||
    (await TroubleshootingGroundingFailure(suppliedAsKnowledge, troubleshootingKnowledge)).SemanticRule != "invalidKnowledgeProvenance" ||
    (await TroubleshootingGroundingFailure(knowledgeAsSupplied, troubleshootingKnowledge)).SemanticRule != "invalidSuppliedEvidenceReference" ||
    (await TroubleshootingGroundingFailure(knowledgeAsInference, troubleshootingKnowledge)).SemanticRule != "invalidKnowledgeProvenance")
    throw new Exception("Troubleshooting knowledge citations or provenance were not enforced.");

var genericHighConfidence = validTroubleshootingOutput
    .Replace("\"failureCategory\":\"unknown\"", "\"failureCategory\":\"authorization\"", StringComparison.Ordinal)
    .Replace("\"level\":\"low\"", "\"level\":\"high\"", StringComparison.Ordinal);
var genericRequest = troubleshootingRequest with { ErrorText = "Deployment failed without additional details." };
if ((await TroubleshootingGroundingFailure(genericHighConfidence, troubleshootingKnowledge, genericRequest)).SemanticRule != "unsupportedHighConfidence")
    throw new Exception("Retrieved guidance upgraded generic evidence to unsupported high confidence.");

foreach (var blockedStatus in new[] { KnowledgeRetrievalStatus.KnowledgeConflict, KnowledgeRetrievalStatus.StaleKnowledge })
{
    var blockedCalls = 0;
    var blockedService = new FoundryDeploymentTroubleshootingService(
        new FakeFoundryTroubleshootingClient((_, _) => { blockedCalls++; return Task.FromResult(new FoundryTroubleshootingCompletion(validTroubleshootingOutput, false, true)); }),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
        new FakeKnowledgeRetriever(new(blockedStatus, [], [], [])));
    try { await blockedService.TroubleshootAsync(troubleshootingRequest, CancellationToken.None); }
    catch (AiReviewException error) when (error.FailureStage == "knowledgeRetrieval" && blockedCalls == 0) { continue; }
    throw new Exception("Unsafe troubleshooting knowledge state did not block the model call.");
}
if (troubleshootingResult.KnowledgeGrounding.Status != KnowledgeRetrievalStatus.NoRelevantKnowledge ||
    troubleshootingResult.KnowledgeGrounding.Citations.Count != 0 ||
    !DeploymentTroubleshootingJsonSchema.Create(sanitizedAuthorization, troubleshootingKnowledge)
        .Contains(troubleshootingChunk.ChunkId, StringComparison.Ordinal))
    throw new Exception("No-knowledge behavior or exact troubleshooting citation schema is invalid.");
Console.WriteLine("PASS: Foundry troubleshooting schema, evidence rules, provider failures, and unsafe model-output rejection");

var healthRequest = new CreateApplicationHealthAnalysisRequest(
    "Why did this App Service become slow and return server errors?",
    new("inventory-api", "dev", "appservice", "app-inventory-example-dev", "rg-aidp-example-dev", "southcentralus"),
    DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow,
    [new(ApplicationHealthEvidenceType.ApplicationInsights, DateTimeOffset.UtcNow.AddMinutes(-5), "Request failures",
        "Application telemetry reports elevated request failures and dependency timeouts.",
        [new(HealthMetricName.Http5xx, HealthMetricAggregation.Count, 42, "count", DateTimeOffset.UtcNow.AddMinutes(-5))])]);

async Task<HttpResponseMessage> Health(object body, string? token = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/health/analyze") { Content = JsonContent.Create(body) };
    if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return await client.SendAsync(request);
}
if ((await Health(healthRequest)).StatusCode != HttpStatusCode.Unauthorized ||
    (await Health(healthRequest, Token(null, "AIDP.Provisioner"))).StatusCode != HttpStatusCode.Forbidden)
    throw new Exception("Health analysis did not require SubmitRequest authorization.");
if ((await Health(healthRequest, authorizedToken)).StatusCode != HttpStatusCode.OK)
    throw new Exception("Valid deterministic health analysis failed.");
async Task<HttpResponseMessage> LiveHealth(object body, string? token = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/health/analyze-live") { Content = JsonContent.Create(body) };
    if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return await client.SendAsync(request);
}
var liveHealthRequest = new CreateLiveApplicationHealthAnalysisRequest("app-inventory-example-dev",
    "Assess the allowlisted application using the controlled Azure Monitor profile.");
if ((await LiveHealth(liveHealthRequest)).StatusCode != HttpStatusCode.Unauthorized ||
    (await LiveHealth(liveHealthRequest, Token(null, "AIDP.Provisioner"))).StatusCode != HttpStatusCode.Forbidden ||
    (await LiveHealth(liveHealthRequest, authorizedToken)).StatusCode != HttpStatusCode.OK ||
    (await LiveHealth(liveHealthRequest with { ApplicationName = "unknown-app" }, authorizedToken)).StatusCode != HttpStatusCode.NotFound)
    throw new Exception("Live health analysis authorization or allowlist boundary failed.");
Console.WriteLine("PASS: live health analysis requires SubmitRequest authorization and an allowlisted application");
foreach (var invalidHealth in new CreateApplicationHealthAnalysisRequest[]
{
    healthRequest with { Question = "too short" },
    healthRequest with { WindowStart = DateTimeOffset.UtcNow.AddDays(-8), WindowEnd = DateTimeOffset.UtcNow },
    healthRequest with { Target = healthRequest.Target with { AzureService = "functionapp" } },
    healthRequest with { Evidence = [healthRequest.Evidence[0] with { Metrics = [new(HealthMetricName.CpuPercentage, HealthMetricAggregation.Average, double.NaN, "percent", null)] }] },
    healthRequest with { Evidence = [healthRequest.Evidence[0] with { Metrics = [new(HealthMetricName.CpuPercentage, HealthMetricAggregation.Average, 50, "count", null)] }] }
})
    if (ApplicationHealthAnalysisValidator.Validate(invalidHealth).Count == 0)
        throw new Exception("Invalid health-analysis input was accepted.");

var secretHealth = healthRequest with
{
    Evidence = [healthRequest.Evidence[0] with { Content =
        "Authorization: Bearer eyJ12345678.abcdefgh.ijklmnop user@example.com https://example.test/path?sig=secret /subscriptions/00000000-0000-0000-0000-000000000000" }]
};
var sanitizedHealth = ApplicationHealthEvidenceSanitizer.Sanitize(secretHealth);
if (!sanitizedHealth.RedactionsApplied || sanitizedHealth.Evidence[0].Content.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
    sanitizedHealth.Evidence[0].Content.Contains("user@example.com") || sanitizedHealth.Evidence[0].Content.Contains("ac993729"))
    throw new Exception("Health evidence sanitizer exposed sensitive input.");
if (sanitizedHealth.Evidence[0].Id != "evidence-001" || sanitizedHealth.Evidence[0].Metrics[0].Id != "metric-001")
    throw new Exception("Health evidence IDs were not deterministic.");

var healthInput = ApplicationHealthEvidenceSanitizer.Sanitize(healthRequest);
var healthSchema = ApplicationHealthJsonSchema.Create(healthInput,
    new(KnowledgeRetrievalStatus.NoRelevantKnowledge, [], [], []));
if (!healthSchema.Contains("\"evidence-001\"") || !healthSchema.Contains("\"metric-001\"") || healthSchema.Contains("\"metric-002\""))
    throw new Exception("Health schema did not constrain references to the current request.");
var low = new HealthConfidence(HealthConfidenceLevel.Low, "Evidence is limited.");
var high = new HealthConfidence(HealthConfidenceLevel.High, "Direct evidence supports this conclusion.");
ApplicationHealthEvidence Evidence(HealthEvidenceProvenance provenance, string? reference, string statement = "The supplied evidence supports this observation.") => new(provenance, statement, reference);
ApplicationHealthFinding Finding(ApplicationHealthEvidence evidence, HealthSeverity severity = HealthSeverity.Medium,
    HealthConclusionType conclusion = HealthConclusionType.Finding, HealthConfidence? confidence = null) =>
    new("A health signal requires investigation.", ApplicationHealthCategory.HttpErrors, severity, conclusion,
        [evidence], confidence ?? low, "Review the corresponding bounded evidence source.");
ApplicationHealthModelOutput Output(OverallHealthAssessment assessment, ApplicationHealthFinding finding, HealthConfidence? confidence = null) =>
    new("The supplied health evidence requires review.", assessment, [finding], ["Dependency state is unknown."],
        ["Review the supplied health signals."], confidence ?? low);

if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Degraded, Finding(Evidence(HealthEvidenceProvenance.SuppliedApplicationInsights, "evidence-001"))), healthInput) is not null)
    throw new Exception("Valid health evidence was rejected.");
if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Degraded, Finding(Evidence(HealthEvidenceProvenance.SuppliedAppServiceLog, "evidence-001"))), healthInput) != "invalidEvidenceProvenance")
    throw new Exception("Health provenance mismatch was accepted.");
if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Degraded, Finding(Evidence(HealthEvidenceProvenance.ModelInference, "evidence-001"))), healthInput) != "invalidModelInferenceSource")
    throw new Exception("Model inference with a source reference was accepted.");
if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Degraded, Finding(Evidence(HealthEvidenceProvenance.PlatformKnownFact, "AIDP trusted context", "Invented platform fact."))), healthInput) != "invalidPlatformKnownFact")
    throw new Exception("Invented platform fact was accepted.");
if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Unknown, Finding(Evidence(HealthEvidenceProvenance.ModelInference, null)), high), healthInput) != "unknownConfidenceMismatch")
    throw new Exception("Unknown health with high confidence was accepted.");
if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Healthy, Finding(Evidence(HealthEvidenceProvenance.SuppliedApplicationInsights, "evidence-001"))), healthInput) != "unsupportedHealthAssessment")
    throw new Exception("Healthy status without direct availability evidence was accepted.");
if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Unhealthy, Finding(Evidence(HealthEvidenceProvenance.ModelInference, null))), healthInput) != "unsupportedHealthAssessment")
    throw new Exception("Unhealthy status based only on inference was accepted.");
if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Degraded, Finding(Evidence(HealthEvidenceProvenance.ModelInference, null), HealthSeverity.Critical)), healthInput) != "unsupportedHealthAssessment")
    throw new Exception("Critical inference-only finding was accepted.");
if (ApplicationHealthAnalysisValidator.GetSemanticRule(
        Output(OverallHealthAssessment.Degraded, Finding(Evidence(HealthEvidenceProvenance.SuppliedApplicationInsights, "evidence-001"),
            conclusion: HealthConclusionType.ConfirmedCause, confidence: low)), healthInput) != "confirmedCauseInsufficientEvidence")
    throw new Exception("Low-confidence confirmed cause was accepted.");

var healthJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
healthJsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
var validHealthOutput = Output(OverallHealthAssessment.Degraded,
    Finding(Evidence(HealthEvidenceProvenance.SuppliedApplicationInsights, "evidence-001")));
var validHealthJson = JsonSerializer.Serialize(validHealthOutput, healthJsonOptions);
var foundryHealth = new FoundryApplicationHealthAnalysisService(
    new FakeFoundryHealthClient((_, _) => Task.FromResult(new FoundryHealthCompletion(validHealthJson, false, true))),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
var trustedHealth = await foundryHealth.AnalyzeAsync(healthRequest, CancellationToken.None);
if (trustedHealth.AnalysisId == Guid.Empty || trustedHealth.SuppliedEvidence.EvidenceItemCount != 1 || trustedHealth.SuppliedEvidence.ApplicationName != "inventory-api")
    throw new Exception("Health service did not construct the trusted envelope.");

var mismatchedHealthOutput = Output(OverallHealthAssessment.Degraded,
    Finding(Evidence(HealthEvidenceProvenance.SuppliedAppServiceLog, "evidence-001")));
var mismatchedHealthJson = JsonSerializer.Serialize(mismatchedHealthOutput, healthJsonOptions);
var provenanceService = new FoundryApplicationHealthAnalysisService(
    new FakeFoundryHealthClient((_, _) => Task.FromResult(new FoundryHealthCompletion(mismatchedHealthJson, false, true))),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
var provenanceResult = await provenanceService.AnalyzeAsync(healthRequest, CancellationToken.None);
if (provenanceResult.Findings[0].Evidence[0].Provenance != HealthEvidenceProvenance.SuppliedApplicationInsights)
    throw new Exception("Health service trusted model-selected evidence provenance over the supplied evidence catalog.");

var healthQuery = ApplicationHealthKnowledgeQuery.Create(healthInput);
var healthKnowledge = knowledgeRetriever.Retrieve(healthQuery);
if (healthQuery.Environment != "dev" || healthQuery.WorkloadType != "appservice" || healthQuery.MaximumChunks != 5 ||
    !healthQuery.Categories.SetEquals(new[]
    {
        KnowledgeCategory.HealthRunbook, KnowledgeCategory.OperationalProcedure,
        KnowledgeCategory.TroubleshootingRunbook, KnowledgeCategory.ArchitectureStandard,
        KnowledgeCategory.KnownLimitation
    }) || !healthQuery.ControlledTerms.Contains("http5xx", StringComparer.OrdinalIgnoreCase) ||
    !healthQuery.ControlledTerms.Contains("latency", StringComparer.OrdinalIgnoreCase) ||
    healthKnowledge.Status != KnowledgeRetrievalStatus.Success ||
    !healthKnowledge.Chunks.Any(chunk => chunk.Category == KnowledgeCategory.HealthRunbook))
    throw new Exception("Health knowledge query did not retrieve fixed, scoped 5xx and latency guidance.");

var dependencyHealthRequest = healthRequest with
{
    Question = "Did latency increase because a backend dependency became slow after deployment?",
    Evidence =
    [
        new(ApplicationHealthEvidenceType.ApplicationInsights, DateTimeOffset.UtcNow, "Dependency latency",
            "Supplied telemetry contains latency and dependency observations.",
            [new(HealthMetricName.AverageResponseTime, HealthMetricAggregation.Average, 900, "ms", DateTimeOffset.UtcNow),
             new(HealthMetricName.DependencyFailures, HealthMetricAggregation.Count, 4, "count", DateTimeOffset.UtcNow)]),
        new(ApplicationHealthEvidenceType.DeploymentMetadata, DateTimeOffset.UtcNow, "Deployment timing",
            "A deployment occurred within the supplied observation window.", [])
    ]
};
var dependencyKnowledge = knowledgeRetriever.Retrieve(ApplicationHealthKnowledgeQuery.Create(
    ApplicationHealthEvidenceSanitizer.Sanitize(dependencyHealthRequest)));
if (!dependencyKnowledge.Chunks.Any(chunk => chunk.Category == KnowledgeCategory.OperationalProcedure) ||
    !dependencyKnowledge.Chunks.Any(chunk => chunk.Category == KnowledgeCategory.HealthRunbook))
    throw new Exception("Latency, dependency, and deployment metadata did not retrieve operational and health guidance.");

var healthChunk = healthKnowledge.Chunks.First(chunk => chunk.Category == KnowledgeCategory.HealthRunbook);
var groundedHealthOutput = Output(OverallHealthAssessment.Degraded,
    Finding(Evidence(HealthEvidenceProvenance.TrustedPlatformKnowledge, healthChunk.ChunkId,
        "Retrieved guidance describes elevated 5xx responses as a symptom.")));
var groundedHealthJson = JsonSerializer.Serialize(groundedHealthOutput, healthJsonOptions);
var healthModelCalls = 0;
var groundedHealthService = new FoundryApplicationHealthAnalysisService(
    new FakeFoundryHealthClient((input, _) =>
    {
        healthModelCalls++;
        if (!input.Knowledge.Chunks.Any(chunk => chunk.ChunkId == healthChunk.ChunkId))
            throw new Exception("Foundry health analysis did not receive the trusted retrieval result.");
        return Task.FromResult(new FoundryHealthCompletion(groundedHealthJson, false, true));
    }),
    Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
    new FakeKnowledgeRetriever(healthKnowledge));
var groundedHealthResult = await groundedHealthService.AnalyzeAsync(healthRequest, CancellationToken.None);
var healthCitation = groundedHealthResult.KnowledgeGrounding.Citations.Single();
if (healthModelCalls != 1 || healthCitation.ChunkId != healthChunk.ChunkId ||
    healthCitation.DocumentId != healthChunk.DocumentId ||
    groundedHealthResult.KnowledgeGrounding.RetrievedChunkCount != healthKnowledge.Chunks.Count)
    throw new Exception("Health grounding metadata was not resolved from the trusted API catalog.");

async Task<AiReviewException> HealthGroundingFailure(string json, KnowledgeRetrievalResult retrieval,
    CreateApplicationHealthAnalysisRequest? suppliedRequest = null)
{
    var service = new FoundryApplicationHealthAnalysisService(
        new FakeFoundryHealthClient((_, _) => Task.FromResult(new FoundryHealthCompletion(json, false, true))),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
        new FakeKnowledgeRetriever(retrieval));
    try { await service.AnalyzeAsync(suppliedRequest ?? healthRequest, CancellationToken.None); }
    catch (AiReviewException error) { return error; }
    throw new Exception("Invalid health knowledge grounding was accepted.");
}

var inventedHealthCitation = groundedHealthJson.Replace(healthChunk.ChunkId,
    "knowledge:invented@1.0.0#fake:01", StringComparison.Ordinal);
var otherHealthChunk = knowledgeCatalog.Chunks.First(chunk =>
    healthKnowledge.Chunks.All(retrieved => retrieved.ChunkId != chunk.ChunkId));
var nonRetrievedHealthCitation = groundedHealthJson.Replace(healthChunk.ChunkId, otherHealthChunk.ChunkId, StringComparison.Ordinal);
var suppliedAsHealthKnowledge = groundedHealthJson.Replace(healthChunk.ChunkId, "evidence-001", StringComparison.Ordinal);
var knowledgeAsHealthEvidence = groundedHealthJson.Replace("\"provenance\":\"trustedPlatformKnowledge\"",
    "\"provenance\":\"suppliedApplicationInsights\"", StringComparison.Ordinal);
var knowledgeAsHealthInference = groundedHealthJson.Replace("\"provenance\":\"trustedPlatformKnowledge\"",
    "\"provenance\":\"modelInference\"", StringComparison.Ordinal);
if ((await HealthGroundingFailure(inventedHealthCitation, healthKnowledge)).SemanticRule != "invalidKnowledgeCitation" ||
    (await HealthGroundingFailure(nonRetrievedHealthCitation, healthKnowledge with { Chunks = [healthChunk] })).SemanticRule != "invalidKnowledgeCitation" ||
    (await HealthGroundingFailure(suppliedAsHealthKnowledge, healthKnowledge)).SemanticRule != "invalidKnowledgeProvenance" ||
    (await HealthGroundingFailure(knowledgeAsHealthEvidence, healthKnowledge)).SemanticRule != "invalidKnowledgeProvenance" ||
    (await HealthGroundingFailure(knowledgeAsHealthInference, healthKnowledge)).SemanticRule != "invalidKnowledgeProvenance")
    throw new Exception("Health knowledge citations or provenance were not enforced.");

var direct5xx = Evidence(HealthEvidenceProvenance.SuppliedApplicationInsights, "evidence-001",
    "The supplied telemetry reports HTTP 5xx responses.");
var knowledgeEvidence = Evidence(HealthEvidenceProvenance.TrustedPlatformKnowledge, healthChunk.ChunkId,
    "The runbook describes HTTP 5xx elevation as a symptom.");
var confirmedFrom5xx = Output(OverallHealthAssessment.Degraded,
    new("A root cause was identified.", ApplicationHealthCategory.HttpErrors, HealthSeverity.High,
        HealthConclusionType.ConfirmedCause, [direct5xx, knowledgeEvidence], high,
        "Review the direct evidence."), high);
var unhealthyFromKnowledge = Output(OverallHealthAssessment.Unhealthy, Finding(knowledgeEvidence));
var healthyFromKnowledge = Output(OverallHealthAssessment.Healthy, Finding(knowledgeEvidence));
var criticalFromKnowledge = Output(OverallHealthAssessment.Degraded, Finding(knowledgeEvidence, HealthSeverity.Critical));
if ((await HealthGroundingFailure(JsonSerializer.Serialize(confirmedFrom5xx, healthJsonOptions), healthKnowledge)).SemanticRule != "confirmedCauseInsufficientEvidence" ||
    (await HealthGroundingFailure(JsonSerializer.Serialize(unhealthyFromKnowledge, healthJsonOptions), healthKnowledge)).SemanticRule != "unsupportedHealthAssessment" ||
    (await HealthGroundingFailure(JsonSerializer.Serialize(healthyFromKnowledge, healthJsonOptions), healthKnowledge)).SemanticRule != "unsupportedHealthAssessment" ||
    (await HealthGroundingFailure(JsonSerializer.Serialize(criticalFromKnowledge, healthJsonOptions), healthKnowledge)).SemanticRule != "unsupportedHealthAssessment")
    throw new Exception("Retrieved health guidance improperly established cause, health status, or critical severity.");

foreach (var blockedStatus in new[] { KnowledgeRetrievalStatus.KnowledgeConflict, KnowledgeRetrievalStatus.StaleKnowledge })
{
    var blockedCalls = 0;
    var blockedHealth = new FoundryApplicationHealthAnalysisService(
        new FakeFoundryHealthClient((_, _) => { blockedCalls++; return Task.FromResult(new FoundryHealthCompletion(validHealthJson, false, true)); }),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }),
        new FakeKnowledgeRetriever(new(blockedStatus, [], [], [])));
    try { await blockedHealth.AnalyzeAsync(healthRequest, CancellationToken.None); }
    catch (AiReviewException error) when (error.FailureStage == "knowledgeRetrieval" && blockedCalls == 0) { continue; }
    throw new Exception("Unsafe health knowledge state did not block the model call.");
}
if (trustedHealth.KnowledgeGrounding.Status != KnowledgeRetrievalStatus.NoRelevantKnowledge ||
    trustedHealth.KnowledgeGrounding.Citations.Count != 0 ||
    !ApplicationHealthJsonSchema.Create(healthInput, healthKnowledge).Contains(healthChunk.ChunkId, StringComparison.Ordinal))
    throw new Exception("No-knowledge health behavior or exact citation schema is invalid.");

async Task<AiReviewException> HealthFailure(string json, bool refused = false, bool complete = true)
{
    var service = new FoundryApplicationHealthAnalysisService(
        new FakeFoundryHealthClient((_, _) => Task.FromResult(new FoundryHealthCompletion(json, refused, complete))),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
    try { await service.AnalyzeAsync(healthRequest, CancellationToken.None); }
    catch (AiReviewException error) { return error; }
    throw new Exception("Unsafe health output was accepted.");
}
var credentialOutput = validHealthJson.Replace("A health signal requires investigation.", "password=supersecret", StringComparison.Ordinal);
var actionOutput = validHealthJson.Replace("Review the corresponding bounded evidence source.", "The system restarted the App Service.", StringComparison.Ordinal);
var queryOutput = validHealthJson.Replace("Review the corresponding bounded evidence source.", "I queried Azure and confirmed the state.", StringComparison.Ordinal);
foreach (var failure in new[]
{
    await HealthFailure(credentialOutput), await HealthFailure(actionOutput), await HealthFailure(queryOutput),
    await HealthFailure("{\"broken\":"), await HealthFailure(validHealthJson, refused: true), await HealthFailure(validHealthJson, complete: false)
})
    if (failure.StatusCode != 502) throw new Exception("Health model-output failure was not mapped safely.");
foreach (var status in new[] { 401, 403, 429, 500 })
    if (FoundryApplicationHealthAnalysisService.ClassifyProviderStatus(status).StatusCode != 503)
        throw new Exception("Health provider status was not mapped safely.");
foreach (var providerFailure in new (Exception Error, string Code, int Status)[]
{
    (new OperationCanceledException("private timeout detail"), "healthAnalysisTimeout", 504),
    (new AuthenticationFailedException("private credential detail"), "healthAnalysisAuthenticationFailed", 503),
    (new HttpRequestException("private transport detail"), "healthAnalysisProviderUnavailable", 503)
})
{
    var service = new FoundryApplicationHealthAnalysisService(
        new FakeFoundryHealthClient((_, _) => Task.FromException<FoundryHealthCompletion>(providerFailure.Error)),
        Options.Create(new AiReviewOptions { Provider = "Foundry", Endpoint = "https://example.openai.azure.com/openai/v1/", ModelDeployment = "test", TimeoutSeconds = 30 }), noKnowledgeRetriever);
    try { await service.AnalyzeAsync(healthRequest, CancellationToken.None); }
    catch (AiReviewException error) when (error.ErrorCode == providerFailure.Code && error.StatusCode == providerFailure.Status &&
        !error.ToString().Contains("private", StringComparison.OrdinalIgnoreCase)) { continue; }
    throw new Exception("Health provider exception was not safely mapped.");
}
Console.WriteLine("PASS: Phase 5C validation, sanitization, provenance, strict schema, semantic rules, and provider failures");

Environment.SetEnvironmentVariable("AiReview__Provider", "Foundry");
Environment.SetEnvironmentVariable("AiReview__Endpoint", "https://aif-aidp-example-dev.openai.azure.com/openai/v1/");
Environment.SetEnvironmentVariable("AiReview__ModelDeployment", "gpt-5.4-mini");
Environment.SetEnvironmentVariable("AiReview__TimeoutSeconds", "30");
using (var foundryFactory = new WebApplicationFactory<Aidp.Api.Models.ApplicationRequest>().WithWebHostBuilder(builder =>
    builder.UseEnvironment("Production")))
{
    if (foundryFactory.Services.GetRequiredService<IAiInfrastructureReviewService>() is not FoundryAiInfrastructureReviewService)
        throw new Exception("Production Foundry configuration did not select the Foundry provider.");
    if (foundryFactory.Services.GetRequiredService<IApplicationHealthAnalysisService>() is not FoundryApplicationHealthAnalysisService)
        throw new Exception("Production Foundry configuration did not select the Foundry health provider.");
}
Environment.SetEnvironmentVariable("AiReview__Provider", null);
Environment.SetEnvironmentVariable("AiReview__Endpoint", null);
Environment.SetEnvironmentVariable("AiReview__ModelDeployment", null);
Environment.SetEnvironmentVariable("AiReview__TimeoutSeconds", null);
var invalidProductionRejected = false;
try
{
    using var invalidFactory = new WebApplicationFactory<Aidp.Api.Models.ApplicationRequest>().WithWebHostBuilder(builder =>
        builder.UseEnvironment("Production"));
    _ = invalidFactory.Services.GetRequiredService<IAiInfrastructureReviewService>();
}
catch (OptionsValidationException)
{
    invalidProductionRejected = true;
}
if (!invalidProductionRejected) throw new Exception("Production silently accepted missing Foundry configuration.");
Console.WriteLine("PASS: Foundry registration works and Production rejects missing configuration");

Console.WriteLine("All HTTP authentication/authorization and AI review checks passed.");

var disabledRecord = await accepted.Content.ReadFromJsonAsync<ApplicationRequest>();
if (disabledRecord?.Status != "validated" || disabledRecord.PipelineRunId is not null)
    throw new Exception("Disabled queue changed the request.");
Console.WriteLine("PASS: disabled queue stays validated without HTTP");

foreach (var scenario in new[] { "accepted", "rejected", "timeout", "connection", "malformed", "incomplete", "wrongPipeline", "serverError", "badRequest", "unauthorized", "requestTimeout", "redirect", "empty" })
{
    var diagnostics = new QueueLogCapture();
    var handler = new FakeQueueHandler(_ => scenario switch
    {
        "accepted" => new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":123,\"pipeline\":{\"id\":3}}") },
        "rejected" => new(HttpStatusCode.Forbidden) { Content = new StringContent("private upstream detail") },
        "badRequest" => new(HttpStatusCode.BadRequest),
        "unauthorized" => new(HttpStatusCode.Unauthorized),
        "requestTimeout" => new(HttpStatusCode.RequestTimeout),
        "redirect" => new(HttpStatusCode.Redirect),
        "empty" => new(HttpStatusCode.OK) { Content = new StringContent("") },
        "timeout" => throw new TaskCanceledException("private upstream detail"),
        "connection" => throw new HttpRequestException("Authorization: Bearer test-credential"),
        "malformed" => new(HttpStatusCode.OK) { Content = new StringContent("private upstream detail") },
        "incomplete" => new(HttpStatusCode.OK) { Content = new StringContent("{}") },
        "wrongPipeline" => new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":123,\"pipeline\":{\"id\":2}}") },
        _ => new(HttpStatusCode.InternalServerError)
    });
    using var queueFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.PostConfigure<AzureDevOpsOptions>(options => options.QueueEnabled = true);
        services.AddSingleton<ILogger<AzureDevOpsQueueClient>>(diagnostics);
        services.AddHttpClient<AzureDevOpsQueueClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
    }));
    using var queueHttp = queueFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    // Authentication and validation must run before the queue client.
    var body = new { applicationName = "inventory-api", environment = "dev", runtime = "dotnet10", resourceType = "appservice" };
    var anonymous = await queueHttp.PostAsJsonAsync("/api/requests", body);
    if (anonymous.StatusCode != HttpStatusCode.Unauthorized || handler.Calls != 0) throw new Exception("Anonymous request queued.");
    queueHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("Requests.Submit", null));
    var forbidden = await queueHttp.PostAsJsonAsync("/api/requests", body);
    if (forbidden.StatusCode != HttpStatusCode.Forbidden || handler.Calls != 0) throw new Exception("Unauthorized request queued.");
    queueHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("Requests.Submit", "AIDP.Provisioner"));
    var invalid = await queueHttp.PostAsJsonAsync("/api/requests", new { applicationName = "aidp", environment = "dev", runtime = "dotnet10", resourceType = "appservice" });
    if (invalid.StatusCode != HttpStatusCode.BadRequest || handler.Calls != 0) throw new Exception("Invalid request queued.");
    // Additional caller-controlled fields must not influence the outgoing payload.
    var response = await queueHttp.PostAsJsonAsync("/api/requests", new
    {
        applicationName = "inventory-api", environment = "dev", runtime = "dotnet10", resourceType = "appservice",
        pipelineId = 2, branch = "refs/heads/evil", variables = new { dangerous = "value" }, yamlOverride = "evil"
    });
    if (handler.Calls != 1) throw new Exception("Queue was not called exactly once.");
    Guid requestId;
    if (scenario == "accepted")
    {
        if (response.StatusCode != HttpStatusCode.Created) throw new Exception("Accepted run failed.");
        var record = (await response.Content.ReadFromJsonAsync<ApplicationRequest>())!;
        requestId = record.RequestId;
        if (record.Status != "queued" || record.PipelineRunId != 123 || record.QueuedAt is null || record.QueueOutcome != "accepted")
            throw new Exception("Accepted run was not recorded.");
    }
    else
    {
        if (response.StatusCode != HttpStatusCode.BadGateway) throw new Exception("Queue failure silently succeeded.");
        var text = await response.Content.ReadAsStringAsync();
        if (text.Contains("private upstream detail") || text.Contains("test-credential")) throw new Exception("Sensitive response escaped.");
        using var problem = JsonDocument.Parse(text);
        requestId = problem.RootElement.GetProperty("requestId").GetGuid();
    }
    var saved = (await queueHttp.GetFromJsonAsync<ApplicationRequest>($"/api/requests/{requestId}"))!;
    if (scenario == "accepted" && saved.StatusRefreshError != "statusLookupFailed")
        throw new Exception("GET did not safely report the mocked status lookup failure.");
    var expected = scenario == "accepted" ? "queued" : scenario is "rejected" or "badRequest" or "unauthorized" ? "failed" : "validated";
    if (saved.Status != expected || (scenario != "accepted" && (saved.PipelineRunId is not null || saved.QueuedAt is not null)))
        throw new Exception("Incorrect saved status.");
    if (scenario is not ("accepted" or "rejected" or "badRequest" or "unauthorized") && saved.QueueOutcome != "unknown") throw new Exception("Ambiguous outcome misreported.");
    var expectedCategory = scenario switch
    {
        "accepted" => "none",
        "rejected" => "authorization",
        "unauthorized" => "authentication",
        "badRequest" => "explicitRejection",
        "timeout" or "requestTimeout" => "timeout",
        "connection" => "transportError",
        "malformed" or "empty" => "malformedResponse",
        "incomplete" or "wrongPipeline" => "invalidRunResponse",
        "redirect" => "redirect",
        _ => "serverError"
    };
    int? expectedHttpStatus = scenario switch
    {
        "timeout" or "connection" => null,
        "rejected" => 403, "unauthorized" => 401, "badRequest" => 400,
        "requestTimeout" => 408, "redirect" => 302, "serverError" => 500,
        _ => 200
    };
    if (diagnostics.Entries.Count != 1) throw new Exception("Expected one queue diagnostic.");
    var entry = diagnostics.Entries.Single();
    var allowedFields = new[] { "RequestId", "QueueOutcome", "ErrorCode", "HttpStatusCode", "ElapsedMilliseconds", "ResponseReceived", "FailureCategory", "{OriginalFormat}" };
    if (!entry.Keys.Order().SequenceEqual(allowedFields.Order()) ||
        !Equals(entry["RequestId"], requestId) || !Equals(entry["QueueOutcome"], saved.QueueOutcome) ||
        !Equals(entry["ErrorCode"], saved.QueueErrorCode) || !Equals(entry["FailureCategory"], expectedCategory) ||
        !Equals(entry["HttpStatusCode"], expectedHttpStatus) ||
        !Equals(entry["ResponseReceived"], expectedHttpStatus.HasValue) ||
        entry["ElapsedMilliseconds"] is not long milliseconds || milliseconds < 0)
        throw new Exception("Incorrect structured queue diagnostic.");
    var store = queueFactory.Services.GetRequiredService<InMemoryApplicationRequestStore>();
    if (store.TryBeginQueue(requestId, out _)) throw new Exception("Completed attempt can be requeued.");
    Console.WriteLine($"PASS: queue {scenario}; exactly one call; invalid/unauthorized requests never queue");
}
var concurrentStore = new InMemoryApplicationRequestStore();
var concurrentRequest = concurrentStore.Create(new("appservice", "inventory-api", "dotnet10", "dev", null));
var claims = 0;
Parallel.For(0, 20, i => { if (concurrentStore.TryBeginQueue(concurrentRequest.RequestId, out _)) Interlocked.Increment(ref claims); });
if (claims != 1) throw new Exception("Multiple concurrent queue claims accepted.");
Console.WriteLine("PASS: exactly one concurrent queue claim");

await StatusChecks.Run();
KnowledgeChecks.Run();

sealed class FakeCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext context, CancellationToken cancellationToken)
    {
        if (!context.Scopes.SequenceEqual(new[] { "https://app.vssps.visualstudio.com/.default" })) throw new Exception("Wrong token scope.");
        return new("test-credential", DateTimeOffset.UtcNow.AddHours(1));
    }
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(GetToken(context, cancellationToken));
}

sealed class FakeFoundryReviewClient(
    Func<FoundryInfrastructureReviewInput, CancellationToken, Task<FoundryReviewCompletion>> complete) : IFoundryReviewCompletionClient
{
    public Task<FoundryReviewCompletion> CompleteAsync(FoundryInfrastructureReviewInput input, CancellationToken cancellationToken) =>
        complete(input, cancellationToken);
}

sealed class FakeKnowledgeRetriever(KnowledgeRetrievalResult result) : IPlatformKnowledgeRetriever
{
    public int Calls { get; private set; }
    public KnowledgeQuery? LastQuery { get; private set; }
    public KnowledgeRetrievalResult Retrieve(KnowledgeQuery query)
    {
        Calls++;
        LastQuery = query;
        return result;
    }
}

sealed class FakeFoundryTroubleshootingClient(
    Func<FoundryTroubleshootingInput, CancellationToken, Task<FoundryTroubleshootingCompletion>> complete)
    : IFoundryTroubleshootingCompletionClient
{
    public Task<FoundryTroubleshootingCompletion> CompleteAsync(
        FoundryTroubleshootingInput input, CancellationToken cancellationToken) =>
        complete(input, cancellationToken);
}

sealed class FakeFoundryHealthClient(
    Func<FoundryHealthInput, CancellationToken, Task<FoundryHealthCompletion>> complete)
    : IFoundryHealthCompletionClient
{
    public Task<FoundryHealthCompletion> CompleteAsync(
        FoundryHealthInput input, CancellationToken cancellationToken) => complete(input, cancellationToken);
}

sealed class FakeAzureMonitorCollector : IAzureMonitorMetricsCollector
{
    public Task<AzureMetricCollectionResult> CollectAsync(AzureMetricCollectionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AzureMetricCollectionResult(
            new(ApplicationEvidenceSource.AzureMonitorMetrics, request.MetricProfile, request.TimeWindow,
                AzureMetricCollectionStatus.NoData, 1, 1, 0),
            [new("azuremetric-001", HealthMetricName.Requests, HealthMetricAggregation.Total, null,
                "Count", null, AzureMetricCollectionStatus.NoData)]));
}

sealed class FakeApplicationInsightsCollector : IApplicationInsightsHealthCollector
{
    public Task<ApplicationInsightsCollectionResult> CollectAsync(ApplicationInsightsCollectionRequest request,
        CancellationToken cancellationToken) => Task.FromResult(new ApplicationInsightsCollectionResult(
        new("applicationInsights", request.QueryProfile, request.TimeWindow,
            ApplicationInsightsCollectionStatus.NoData, 1, 0, 0), []));
}

sealed class FakeQueueHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    public int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        if (request.Method != HttpMethod.Post || request.RequestUri!.AbsoluteUri != "https://dev.azure.com/example-org/aidp-public-platform/_apis/pipelines/1/runs?api-version=7.1")
            throw new Exception("Unexpected queue destination.");
        if (request.Headers.Authorization?.Scheme != "Bearer") throw new Exception("Missing server authentication.");
        using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root = payload.RootElement;
        if (root.EnumerateObject().Count() != 2 || root.GetProperty("resources").GetProperty("repositories").GetProperty("self").GetProperty("refName").GetString() != "refs/heads/main")
            throw new Exception("Unsafe queue payload.");
        var parameters = root.GetProperty("templateParameters");
        if (parameters.EnumerateObject().Count() != 4 || parameters.GetProperty("applicationName").GetString() != "inventory-api" ||
            parameters.GetProperty("environment").GetString() != "dev" || parameters.GetProperty("runtime").GetString() != "dotnet10" || parameters.GetProperty("resourceType").GetString() != "appservice")
            throw new Exception("Unexpected template parameters.");
        return response(request);
    }
}

sealed class QueueLogCapture : ILogger<AzureDevOpsQueueClient>
{
    public List<Dictionary<string, object?>> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (exception is not null) throw new Exception("Exception included in queue diagnostic.");
        var fields = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(x => x.Key, x => x.Value);
        var output = formatter(state, exception) + JsonSerializer.Serialize(fields);
        foreach (var forbidden in new[] { "test-credential", "Authorization:", "Bearer", "private upstream detail" })
            if (output.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Sensitive data in queue diagnostic.");
        Entries.Add(fields);
    }
}
