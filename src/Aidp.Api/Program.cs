using Aidp.Api.AzureDevOps;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Microsoft.Extensions.Options;
using Aidp.Api.Models;
using Aidp.Api.Services;
using Aidp.Api.Storage;
using Aidp.Api.Validation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddOptions<AzureDevOpsOptions>()
    .Bind(builder.Configuration.GetSection("AzureDevOps"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Organization) &&
        !string.IsNullOrWhiteSpace(options.Project), "Azure DevOps organization and project are required.")
    .Validate(options => options.WorkloadPipelineId == 3, "Only workload pipeline 1 is allowed.")
    .Validate(options => options.WorkloadBranch == "refs/heads/main", "Only the main workload branch is allowed.")
    .ValidateOnStart();
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddOptions<AzureMonitorOptions>()
    .Bind(builder.Configuration.GetSection(AzureMonitorOptions.SectionName))
    .Validate(options => options.TimeoutSeconds is >= 5 and <= 30,
        "Azure Monitor timeout must be between 5 and 30 seconds.")
    .Validate(options => options.Applications.Count > 0 && options.Applications.All(application =>
        application.Environment == "dev" && !string.IsNullOrWhiteSpace(application.ApplicationName) &&
        application.ResourceId.Contains("/providers/Microsoft.Web/sites/", StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(application.ApplicationInsightsResourceId) ||
         application.ApplicationInsightsResourceId.Contains("/providers/Microsoft.Insights/components/", StringComparison.OrdinalIgnoreCase))),
        "Azure Monitor applications must be allowlisted dev App Service resources.")
    .ValidateOnStart();
builder.Services.AddSingleton<IApplicationResourceCatalog, ConfigurationApplicationResourceCatalog>();
builder.Services.AddSingleton(serviceProvider => new MetricsQueryClient(serviceProvider.GetRequiredService<TokenCredential>()));
builder.Services.AddSingleton<IAzureMetricsQueryClient, AzureSdkMetricsQueryClient>();
builder.Services.AddSingleton<IAzureMonitorMetricsCollector, AzureMonitorMetricsCollector>();
builder.Services.AddHttpClient<IAzureResourceHealthCollector, AzureResourceHealthCollector>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<IAzureDeploymentMetadataCollector, AzureDeploymentMetadataCollector>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton(serviceProvider => new LogsQueryClient(serviceProvider.GetRequiredService<TokenCredential>()));
builder.Services.AddSingleton<IApplicationInsightsQueryClient, AzureSdkApplicationInsightsQueryClient>();
builder.Services.AddSingleton<IApplicationInsightsHealthCollector, ApplicationInsightsHealthCollector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILiveApplicationHealthAnalysisService, LiveApplicationHealthAnalysisService>();
builder.Services.AddHttpClient<AzureDevOpsQueueClient>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddHttpClient<AzureDevOpsStatusClient>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters.RoleClaimType = "roles";
    // Entra v1 uses the App ID URI; v2 uses the API client ID as audience.
    options.TokenValidationParameters.ValidAudiences =
        [builder.Configuration["AzureAd:Audience"]!, builder.Configuration["AzureAd:ClientId"]!];
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Provisioner", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("AIDP.Provisioner"));
    options.AddPolicy("SubmitRequest", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("AIDP.Provisioner")
        .RequireAssertion(context => context.User.FindAll("scp")
            .Any(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("Requests.Submit", StringComparer.Ordinal))));
});
builder.Services.AddSingleton<InMemoryApplicationRequestStore>();
builder.Services.AddSingleton(_ =>
{
    var knowledgePath = Path.Combine(AppContext.BaseDirectory, "knowledge");
    try
    {
        return PlatformKnowledgeLoader.Load(knowledgePath,
            builder.Configuration["Knowledge:RepositoryRevision"] ?? "working-tree",
            DateOnly.FromDateTime(DateTime.UtcNow));
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        throw new InvalidOperationException("Required trusted platform knowledge could not be loaded.", error);
    }
});
builder.Services.AddSingleton<IPlatformKnowledgeRetriever, PlatformKnowledgeRetriever>();
builder.Services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<PlatformKnowledgeCatalog>().Status);
var aiReviewProvider = builder.Configuration[$"{AiReviewOptions.SectionName}:Provider"];
builder.Services.AddOptions<AiReviewOptions>()
    .Bind(builder.Configuration.GetSection(AiReviewOptions.SectionName))
    .Validate(options => options.TimeoutSeconds is >= 5 and <= 60,
        "AI review timeout must be between 5 and 60 seconds.")
    .Validate(options => options.Provider is "Stub" or "Foundry",
        "AI review provider must be Stub or Foundry.")
    .Validate(options => options.Provider != "Foundry" ||
        Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrWhiteSpace(options.ModelDeployment),
        "Foundry requires an HTTPS endpoint and model deployment.")
    .Validate(options => !builder.Environment.IsProduction() || options.Provider == "Foundry",
        "Production must use the Foundry AI review provider.")
    .ValidateOnStart();
if (string.Equals(aiReviewProvider, "Foundry", StringComparison.Ordinal))
{
    builder.Services.AddSingleton<IFoundryReviewCompletionClient, FoundryReviewCompletionClient>();
    builder.Services.AddSingleton<IAiInfrastructureReviewService, FoundryAiInfrastructureReviewService>();
    builder.Services.AddSingleton<IFoundryTroubleshootingCompletionClient, FoundryTroubleshootingCompletionClient>();
    builder.Services.AddSingleton<IDeploymentTroubleshootingService, FoundryDeploymentTroubleshootingService>();
    builder.Services.AddSingleton<IFoundryHealthCompletionClient, FoundryHealthCompletionClient>();
    builder.Services.AddSingleton<IApplicationHealthAnalysisService, FoundryApplicationHealthAnalysisService>();
}
else
{
    builder.Services.AddSingleton<IAiInfrastructureReviewService, AiInfrastructureReviewService>();
    builder.Services.AddSingleton<IDeploymentTroubleshootingService, DeploymentTroubleshootingService>();
    builder.Services.AddSingleton<IApplicationHealthAnalysisService, ApplicationHealthAnalysisService>();
}

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
        options.AddPolicy("LocalPortal", policy =>
            policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .AllowAnyHeader()
                .AllowAnyMethod()));
}

var app = builder.Build();
_ = app.Services.GetRequiredService<PlatformKnowledgeCatalog>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("LocalPortal");
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "healthy",
        service = "AIDP API"
    });
})
.WithName("GetHealth")
.AllowAnonymous();

app.MapPost("/api/ai/review", async (CreateAiInfrastructureReviewRequest request,
    IAiInfrastructureReviewService reviews, CancellationToken cancellationToken) =>
{
    var errors = AiInfrastructureReviewValidator.Validate(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    try
    {
        return Results.Ok(await reviews.ReviewAsync(request, cancellationToken));
    }
    catch (AiReviewException error)
    {
        return Results.Problem(statusCode: error.StatusCode, title: "AI infrastructure review failed.",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.ErrorCode,
                ["failureStage"] = error.FailureStage,
                ["providerStatus"] = error.ProviderStatus,
                ["semanticRule"] = error.SemanticRule
            });
    }
})
.WithName("CreateAiInfrastructureReview")
.RequireAuthorization("SubmitRequest");

app.MapPost("/api/ai/troubleshoot", async (CreateDeploymentTroubleshootingRequest request,
    IDeploymentTroubleshootingService troubleshooting, CancellationToken cancellationToken) =>
{
    var errors = DeploymentTroubleshootingValidator.Validate(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    try
    {
        return Results.Ok(await troubleshooting.TroubleshootAsync(request, cancellationToken));
    }
    catch (AiReviewException error)
    {
        return Results.Problem(statusCode: error.StatusCode, title: "Deployment troubleshooting failed.",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.ErrorCode,
                ["failureStage"] = error.FailureStage,
                ["providerStatus"] = error.ProviderStatus,
                ["semanticRule"] = error.SemanticRule
            });
    }
})
.WithName("CreateDeploymentTroubleshooting")
.RequireAuthorization("SubmitRequest");

app.MapPost("/api/ai/health/analyze", async (CreateApplicationHealthAnalysisRequest request,
    IApplicationHealthAnalysisService health, CancellationToken cancellationToken) =>
{
    var errors = ApplicationHealthAnalysisValidator.Validate(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    try
    {
        return Results.Ok(await health.AnalyzeAsync(request, cancellationToken));
    }
    catch (AiReviewException error)
    {
        return Results.Problem(statusCode: error.StatusCode, title: "Application health analysis failed.",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.ErrorCode,
                ["failureStage"] = error.FailureStage,
                ["providerStatus"] = error.ProviderStatus,
                ["semanticRule"] = error.SemanticRule
            });
    }
})
.WithName("CreateApplicationHealthAnalysis")
.RequireAuthorization("SubmitRequest");

app.MapPost("/api/ai/health/analyze-live", async (CreateLiveApplicationHealthAnalysisRequest request,
    ILiveApplicationHealthAnalysisService health, CancellationToken cancellationToken) =>
{
    var errors = LiveApplicationHealthAnalysisValidator.Validate(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    try
    {
        var result = await health.AnalyzeAsync(request, cancellationToken);
        var metricFailure = result.Collection.Status switch
        {
            AzureMetricCollectionStatus.Unauthorized => Results.Problem(statusCode: 503,
                title: "Azure Monitor metrics are unavailable.", extensions: new Dictionary<string, object?>
                { ["errorCode"] = "azureMonitorUnauthorized", ["collectionStatus"] = result.Collection.Status }),
            AzureMetricCollectionStatus.Timeout => Results.Problem(statusCode: 504,
                title: "Azure Monitor metrics collection timed out.", extensions: new Dictionary<string, object?>
                { ["errorCode"] = "azureMonitorTimeout", ["collectionStatus"] = result.Collection.Status }),
            AzureMetricCollectionStatus.Unavailable => Results.Problem(statusCode: 503,
                title: "Azure Monitor metrics are unavailable.", extensions: new Dictionary<string, object?>
                { ["errorCode"] = "azureMonitorUnavailable", ["collectionStatus"] = result.Collection.Status }),
            _ => null
        };
        if (metricFailure is not null) return metricFailure;
        var insightsFailure = result.ApplicationInsightsCollection?.Status switch
        {
            ApplicationInsightsCollectionStatus.Unauthorized => Results.Problem(statusCode: 503,
                title: "Application Insights telemetry is unavailable.", extensions: new Dictionary<string, object?>
                { ["errorCode"] = "applicationInsightsUnauthorized", ["collectionStatus"] = result.ApplicationInsightsCollection.Status }),
            ApplicationInsightsCollectionStatus.Timeout => Results.Problem(statusCode: 504,
                title: "Application Insights collection timed out.", extensions: new Dictionary<string, object?>
                { ["errorCode"] = "applicationInsightsTimeout", ["collectionStatus"] = result.ApplicationInsightsCollection.Status }),
            ApplicationInsightsCollectionStatus.Unavailable => Results.Problem(statusCode: 503,
                title: "Application Insights telemetry is unavailable.", extensions: new Dictionary<string, object?>
                { ["errorCode"] = "applicationInsightsUnavailable", ["collectionStatus"] = result.ApplicationInsightsCollection.Status }),
            ApplicationInsightsCollectionStatus.QueryFailed => Results.Problem(statusCode: 503,
                title: "Application Insights query failed.", extensions: new Dictionary<string, object?>
                { ["errorCode"] = "applicationInsightsQueryFailed", ["collectionStatus"] = result.ApplicationInsightsCollection.Status }),
            _ => null
        };
        return insightsFailure ?? Results.Ok(result);
    }
    catch (UnknownApplicationException)
    {
        return Results.NotFound(new { errorCode = "applicationNotAllowlisted" });
    }
    catch (AiReviewException error)
    {
        return Results.Problem(statusCode: error.StatusCode, title: "Application health analysis failed.",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.ErrorCode, ["failureStage"] = error.FailureStage,
                ["providerStatus"] = error.ProviderStatus, ["semanticRule"] = error.SemanticRule
            });
    }
})
.WithName("CreateLiveApplicationHealthAnalysis")
.RequireAuthorization("SubmitRequest");

app.MapPost("/api/requests", async (CreateApplicationRequest request, InMemoryApplicationRequestStore store,
    IOptions<AzureDevOpsOptions> options, AzureDevOpsQueueClient queue) =>
{
    var errors = ApplicationRequestValidator.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var created = store.Create(request);
    if (options.Value.QueueEnabled)
    {
        if (!store.TryBeginQueue(created.RequestId, out var claimed))
            return Results.Problem(statusCode: 409, title: "Queue attempt already started.");
        var result = await queue.QueueAsync(claimed!);
        created = store.CompleteQueue(claimed!, result);
        if (result.Outcome != "accepted")
        {
            return Results.Problem(statusCode: 502,
                title: result.Outcome == "unknown" ? "Pipeline queue outcome is unknown." : "Pipeline queue request failed.",
                detail: result.Outcome == "unknown"
                    ? "A run may have been created. Do not resubmit until the outcome has been checked."
                    : "The request was saved, but the pipeline was not queued.",
                extensions: new Dictionary<string, object?>
                {
                    ["requestId"] = created.RequestId, ["queueOutcome"] = created.QueueOutcome,
                    ["errorCode"] = created.QueueErrorCode
                });
        }
    }
    return Results.Created($"/api/requests/{created.RequestId}", created);
})
.WithName("CreateApplicationRequest")
.RequireAuthorization("SubmitRequest");

app.MapGet("/api/requests/{requestId}", async (string requestId, InMemoryApplicationRequestStore store, AzureDevOpsStatusClient status) =>
{
    if (!Guid.TryParse(requestId, out var id) || !store.TryGet(id, out var request))
    {
        return Results.NotFound();
    }

    if (request!.PipelineRunId is int runId && request.Status is not ("succeeded" or "failed" or "canceled"))
        request = store.ApplyRefresh(request, await status.RefreshAsync(runId));
    return Results.Ok(request);
})
.WithName("GetApplicationRequest")
.RequireAuthorization("Provisioner");

app.Run();

// Allows the local integration checks to host the real API in memory.
public partial class Program { }
