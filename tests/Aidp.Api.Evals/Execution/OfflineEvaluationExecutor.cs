using System.Diagnostics;
using Aidp.Api.Evals.Contracts;
using Aidp.Api.Models;
using Aidp.Api.Services;
using Microsoft.Extensions.Options;

namespace Aidp.Api.Evals.Execution;

internal sealed record EvaluationExecutionRecord(
    EvaluationCase Case, object? Response, AiReviewException? Error, int ProviderCalls,
    KnowledgeRetrievalResult Knowledge, long LatencyMilliseconds);

internal sealed class OfflineEvaluationExecutor(IPlatformKnowledgeRetriever catalogRetriever)
{
    private static readonly IOptions<AiReviewOptions> Options = Microsoft.Extensions.Options.Options.Create(
        new AiReviewOptions { Provider = "Foundry", Endpoint = "https://offline.invalid/openai/v1/", ModelDeployment = "offline", TimeoutSeconds = 30 });

    internal async Task<EvaluationExecutionRecord> ExecuteAsync(EvaluationCase evaluationCase)
    {
        var retriever = SelectRetriever(evaluationCase.KnowledgeMode);
        var calls = 0;
        object? response = null;
        AiReviewException? error = null;
        KnowledgeRetrievalResult? captured = retriever is FixedKnowledgeRetriever fixedRetriever ? fixedRetriever.Result : null;
        var timer = Stopwatch.StartNew();
        try
        {
            response = evaluationCase.Input switch
            {
                InfrastructureReviewEvaluationInput input => await new FoundryAiInfrastructureReviewService(
                    new OfflineReviewClient((foundryInput, _) =>
                    {
                        calls++; captured = foundryInput.Knowledge;
                        return Task.FromResult(new FoundryReviewCompletion(ProviderFixtures.Create(evaluationCase, foundryInput.Knowledge), false, true));
                    }), Options, retriever).ReviewAsync(input.Request, CancellationToken.None),
                TroubleshootingEvaluationInput input => await new FoundryDeploymentTroubleshootingService(
                    new OfflineTroubleshootingClient((foundryInput, _) =>
                    {
                        calls++; captured = foundryInput.Knowledge;
                        return Task.FromResult(new FoundryTroubleshootingCompletion(ProviderFixtures.Create(evaluationCase, foundryInput.Knowledge), false, true));
                    }), Options, retriever).TroubleshootAsync(input.Request, CancellationToken.None),
                HealthEvaluationInput input => await new FoundryApplicationHealthAnalysisService(
                    new OfflineHealthClient((foundryInput, _) =>
                    {
                        calls++; captured = foundryInput.Knowledge;
                        return Task.FromResult(new FoundryHealthCompletion(ProviderFixtures.Create(evaluationCase, foundryInput.Knowledge), false, true));
                    }), Options, retriever).AnalyzeAsync(input.Request, CancellationToken.None),
                _ => throw new InvalidOperationException("Unsupported evaluation input.")
            };
        }
        catch (AiReviewException exception)
        {
            error = exception;
        }
        timer.Stop();
        return new(evaluationCase, response, error, calls,
            captured ?? new(KnowledgeRetrievalStatus.Unavailable, [], [], []), timer.ElapsedMilliseconds);
    }

    private IPlatformKnowledgeRetriever SelectRetriever(string mode) => mode switch
    {
        "catalog" => catalogRetriever,
        "none" => new FixedKnowledgeRetriever(new(KnowledgeRetrievalStatus.NoRelevantKnowledge, [], [], [])),
        "conflict" => new FixedKnowledgeRetriever(new(KnowledgeRetrievalStatus.KnowledgeConflict, [],
            [new("eval-conflict", [])], [])),
        "stale" => new FixedKnowledgeRetriever(new(KnowledgeRetrievalStatus.StaleKnowledge, [], [],
            [new("eval-stale", "1.0.0", "Synthetic stale document", "synthetic", new(2025, 1, 1), new(2025, 2, 1))])),
        "unsafeKnowledge" => new FixedKnowledgeRetriever(new(KnowledgeRetrievalStatus.KnowledgeConflict, [],
            [new("eval-unsafe-knowledge-rejected", [])], [])),
        _ => throw new InvalidOperationException("Unsupported knowledge mode.")
    };
}

internal sealed class FixedKnowledgeRetriever(KnowledgeRetrievalResult result) : IPlatformKnowledgeRetriever
{
    internal KnowledgeRetrievalResult Result { get; } = result;
    public KnowledgeRetrievalResult Retrieve(KnowledgeQuery query) => Result;
}

internal sealed class OfflineReviewClient(
    Func<FoundryInfrastructureReviewInput, CancellationToken, Task<FoundryReviewCompletion>> completion)
    : IFoundryReviewCompletionClient
{
    public Task<FoundryReviewCompletion> CompleteAsync(FoundryInfrastructureReviewInput input, CancellationToken token) => completion(input, token);
}

internal sealed class OfflineTroubleshootingClient(
    Func<FoundryTroubleshootingInput, CancellationToken, Task<FoundryTroubleshootingCompletion>> completion)
    : IFoundryTroubleshootingCompletionClient
{
    public Task<FoundryTroubleshootingCompletion> CompleteAsync(FoundryTroubleshootingInput input, CancellationToken token) => completion(input, token);
}

internal sealed class OfflineHealthClient(
    Func<FoundryHealthInput, CancellationToken, Task<FoundryHealthCompletion>> completion)
    : IFoundryHealthCompletionClient
{
    public Task<FoundryHealthCompletion> CompleteAsync(FoundryHealthInput input, CancellationToken token) => completion(input, token);
}
