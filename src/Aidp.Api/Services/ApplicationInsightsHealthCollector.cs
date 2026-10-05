using System.Diagnostics;
using System.Globalization;
using Aidp.Api.Models;
using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Options;

namespace Aidp.Api.Services;

internal enum ApplicationInsightsQueryKind { RequestSummary, DependencySummary, FailedRequests, FailedDependencies }

internal sealed record ApplicationInsightsQueryDefinition(
    string QueryId, ApplicationInsightsQueryKind Kind, string Query);

internal sealed record RawApplicationInsightsQueryResult(
    string QueryId, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

internal interface IApplicationInsightsQueryClient
{
    Task<RawApplicationInsightsQueryResult> QueryAsync(
        string workspaceId, ApplicationInsightsQueryDefinition definition,
        DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken);
}

internal sealed class AzureSdkApplicationInsightsQueryClient(LogsQueryClient client) : IApplicationInsightsQueryClient
{
    public async Task<RawApplicationInsightsQueryResult> QueryAsync(
        string workspaceId, ApplicationInsightsQueryDefinition definition,
        DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var response = await client.QueryWorkspaceAsync(workspaceId, definition.Query,
            new QueryTimeRange(start, end), new LogsQueryOptions(), cancellationToken);
        var table = response.Value.Table;
        var rows = table.Rows.Select(row => (IReadOnlyDictionary<string, object?>)table.Columns
            .Select((column, index) => (column.Name, Value: row[index]))
            .ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal)).ToArray();
        return new(definition.QueryId, rows);
    }
}

internal interface IApplicationInsightsHealthCollector
{
    Task<ApplicationInsightsCollectionResult> CollectAsync(
        ApplicationInsightsCollectionRequest request, CancellationToken cancellationToken);
}

internal sealed class ApplicationInsightsHealthCollector(
    IApplicationInsightsQueryClient client, IOptions<AzureMonitorOptions> options, TimeProvider timeProvider)
    : IApplicationInsightsHealthCollector
{
    internal static readonly ApplicationInsightsQueryDefinition Requests = new(
        "appinsights-requests-summary-v1", ApplicationInsightsQueryKind.RequestSummary,
        "union isfuzzy=true (AppRequests | project Success=tobool(Success), DurationMs=todouble(DurationMs)), (datatable(Success:bool, DurationMs:real)[]) | summarize RequestCount=count(), FailedRequestCount=countif(Success == false), AverageRequestDurationMs=avg(DurationMs), P95RequestDurationMs=percentile(DurationMs, 95) | project RequestCount, FailedRequestCount, AverageRequestDurationMs, P95RequestDurationMs");
    internal static readonly ApplicationInsightsQueryDefinition Dependencies = new(
        "appinsights-dependencies-summary-v1", ApplicationInsightsQueryKind.DependencySummary,
        "union isfuzzy=true (AppDependencies | project Success=tobool(Success), DurationMs=todouble(DurationMs)), (datatable(Success:bool, DurationMs:real)[]) | summarize DependencyCount=count(), FailedDependencyCount=countif(Success == false), AverageDependencyDurationMs=avg(DurationMs) | project DependencyCount, FailedDependencyCount, AverageDependencyDurationMs");
    internal static readonly ApplicationInsightsQueryDefinition FailedRequests = new(
        "appinsights-failed-requests-v1", ApplicationInsightsQueryKind.FailedRequests,
        "union isfuzzy=true (AppRequests | project Success=tobool(Success), OperationName=tostring(Name), ResultCode=tostring(ResultCode)), (datatable(Success:bool, OperationName:string, ResultCode:string)[]) | where Success == false | summarize FailureCount=count() by OperationName, ResultCode | top 5 by FailureCount desc | project OperationName, ResultCode, FailureCount");
    internal static readonly ApplicationInsightsQueryDefinition FailedDependencies = new(
        "appinsights-failed-dependencies-v1", ApplicationInsightsQueryKind.FailedDependencies,
        "union isfuzzy=true (AppDependencies | project Success=tobool(Success), OperationName=tostring(Name), ResultCode=tostring(ResultCode)), (datatable(Success:bool, OperationName:string, ResultCode:string)[]) | where Success == false | summarize FailureCount=count() by OperationName, ResultCode | top 5 by FailureCount desc | project OperationName, ResultCode, FailureCount");

    internal static readonly IReadOnlyDictionary<ApplicationInsightsQueryProfile, IReadOnlyList<ApplicationInsightsQueryDefinition>> Profiles =
        new Dictionary<ApplicationInsightsQueryProfile, IReadOnlyList<ApplicationInsightsQueryDefinition>>
        {
            [ApplicationInsightsQueryProfile.ApplicationRequests] = [Requests],
            [ApplicationInsightsQueryProfile.ApplicationDependencies] = [Dependencies],
            [ApplicationInsightsQueryProfile.ApplicationFailures] = [FailedRequests, FailedDependencies],
            [ApplicationInsightsQueryProfile.ApplicationBasicHealth] = [Requests, Dependencies, FailedRequests, FailedDependencies]
        };

    public async Task<ApplicationInsightsCollectionResult> CollectAsync(
        ApplicationInsightsCollectionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Resource.ApplicationInsightsResourceId) ||
            string.IsNullOrWhiteSpace(request.Resource.LogAnalyticsWorkspaceId))
            return Failure(request, 0, ApplicationInsightsCollectionStatus.Unavailable, 0);

        var definitions = Profiles[request.QueryProfile];
        var duration = Window(request.TimeWindow);
        var end = timeProvider.GetUtcNow();
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var results = new List<RawApplicationInsightsQueryResult>();
            foreach (var definition in definitions)
                results.Add(await client.QueryAsync(request.Resource.LogAnalyticsWorkspaceId, definition,
                    end - duration, end, timeout.Token));
            var summaries = CreateSummaries(definitions, results);
            timer.Stop();
            var successful = summaries.Count(item => item.Status == ApplicationInsightsCollectionStatus.Success);
            var status = successful == 0 ? ApplicationInsightsCollectionStatus.NoData :
                summaries.Any(item => item.Status == ApplicationInsightsCollectionStatus.NoData)
                    ? ApplicationInsightsCollectionStatus.Partial : ApplicationInsightsCollectionStatus.Success;
            return new(new("applicationInsights", request.QueryProfile, request.TimeWindow, status,
                definitions.Count, summaries.Count, timer.ElapsedMilliseconds), summaries);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(request, definitions.Count, ApplicationInsightsCollectionStatus.Timeout, timer.ElapsedMilliseconds);
        }
        catch (RequestFailedException error) when (error.Status is 401 or 403)
        {
            return Failure(request, definitions.Count, ApplicationInsightsCollectionStatus.Unauthorized, timer.ElapsedMilliseconds);
        }
        catch (RequestFailedException)
        {
            return Failure(request, definitions.Count, ApplicationInsightsCollectionStatus.QueryFailed, timer.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return Failure(request, definitions.Count, ApplicationInsightsCollectionStatus.Unavailable, timer.ElapsedMilliseconds);
        }
    }

    private static IReadOnlyList<ApplicationInsightsSummary> CreateSummaries(
        IReadOnlyList<ApplicationInsightsQueryDefinition> definitions,
        IReadOnlyList<RawApplicationInsightsQueryResult> results)
    {
        var summaries = new List<ApplicationInsightsSummary>();
        foreach (var definition in definitions)
        {
            var rows = results.Single(result => result.QueryId == definition.QueryId).Rows;
            switch (definition.Kind)
            {
                case ApplicationInsightsQueryKind.RequestSummary:
                    AddAggregate(rows, summaries,
                        ("RequestCount", ApplicationInsightsSignal.RequestCount, "Count"),
                        ("FailedRequestCount", ApplicationInsightsSignal.FailedRequestCount, "Count"),
                        ("AverageRequestDurationMs", ApplicationInsightsSignal.AverageRequestDuration, "ms"),
                        ("P95RequestDurationMs", ApplicationInsightsSignal.P95RequestDuration, "ms"));
                    break;
                case ApplicationInsightsQueryKind.DependencySummary:
                    AddAggregate(rows, summaries,
                        ("DependencyCount", ApplicationInsightsSignal.DependencyCount, "Count"),
                        ("FailedDependencyCount", ApplicationInsightsSignal.FailedDependencyCount, "Count"),
                        ("AverageDependencyDurationMs", ApplicationInsightsSignal.AverageDependencyDuration, "ms"));
                    break;
                case ApplicationInsightsQueryKind.FailedRequests:
                    AddFailures(rows, summaries, ApplicationInsightsSignal.FailedRequestSummary);
                    break;
                case ApplicationInsightsQueryKind.FailedDependencies:
                    AddFailures(rows, summaries, ApplicationInsightsSignal.FailedDependencySummary);
                    break;
            }
        }
        return summaries.Select((summary, index) => summary with { EvidenceId = $"applicationinsights-{index + 1:000}" }).ToArray();
    }

    private static void AddAggregate(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        List<ApplicationInsightsSummary> summaries,
        params (string Column, ApplicationInsightsSignal Signal, string Unit)[] definitions)
    {
        var row = rows.FirstOrDefault();
        var count = row is not null && row.TryGetValue(definitions[0].Column, out var countValue)
            ? Number(countValue) : null;
        var hasTelemetry = count is > 0;
        foreach (var definition in definitions)
        {
            var value = row is not null && row.TryGetValue(definition.Column, out var raw) ? Number(raw) : null;
            summaries.Add(new("", definition.Signal, value, definition.Unit, null, null,
                hasTelemetry && value.HasValue
                    ? ApplicationInsightsCollectionStatus.Success
                    : ApplicationInsightsCollectionStatus.NoData));
        }
    }

    private static void AddFailures(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        List<ApplicationInsightsSummary> summaries, ApplicationInsightsSignal signal)
    {
        foreach (var row in rows.Take(5))
        {
            var value = row.TryGetValue("FailureCount", out var raw) ? Number(raw) : null;
            summaries.Add(new("", signal, value, "Count", Safe(row, "OperationName", 120),
                Safe(row, "ResultCode", 20), value.HasValue ? ApplicationInsightsCollectionStatus.Success : ApplicationInsightsCollectionStatus.NoData));
        }
    }

    private static double? Number(object? value) => value is null ? null :
        double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : null;
    private static string? Safe(IReadOnlyDictionary<string, object?> row, string column, int max) =>
        row.TryGetValue(column, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Replace("\r", " ").Replace("\n", " ") is { } text
                ? text[..Math.Min(text.Length, max)] : null
            : null;
    private static TimeSpan Window(AzureMetricTimeWindow window) => window switch
    {
        AzureMetricTimeWindow.Last30Minutes => TimeSpan.FromMinutes(30),
        AzureMetricTimeWindow.Last2Hours => TimeSpan.FromHours(2),
        AzureMetricTimeWindow.Last24Hours => TimeSpan.FromHours(24),
        _ => throw new ArgumentOutOfRangeException(nameof(window))
    };
    private static ApplicationInsightsCollectionResult Failure(ApplicationInsightsCollectionRequest request,
        int queryCount, ApplicationInsightsCollectionStatus status, long elapsed) =>
        new(new("applicationInsights", request.QueryProfile, request.TimeWindow, status, queryCount, 0, elapsed), []);
}
