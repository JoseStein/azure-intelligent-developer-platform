using System.Diagnostics;
using Aidp.Api.Models;
using Azure;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Options;

namespace Aidp.Api.Services;

internal sealed record AzureMetricDefinition(
    string AzureName, HealthMetricName HealthName, HealthMetricAggregation Aggregation, string Unit);

internal sealed record RawAzureMetric(string Name, string Unit, IReadOnlyList<RawAzureMetricPoint> Points);
internal sealed record RawAzureMetricPoint(DateTimeOffset Timestamp, double? Average, double? Maximum, double? Total);

internal interface IAzureMetricsQueryClient
{
    Task<IReadOnlyList<RawAzureMetric>> QueryAsync(
        string resourceId, IReadOnlyList<string> metricNames, DateTimeOffset start, DateTimeOffset end,
        CancellationToken cancellationToken);
}

internal sealed class AzureSdkMetricsQueryClient(MetricsQueryClient client) : IAzureMetricsQueryClient
{
    public async Task<IReadOnlyList<RawAzureMetric>> QueryAsync(
        string resourceId, IReadOnlyList<string> metricNames, DateTimeOffset start, DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var options = new MetricsQueryOptions
        {
            TimeRange = new QueryTimeRange(start, end),
            Granularity = TimeSpan.FromMinutes(1),
            MetricNamespace = "Microsoft.Web/sites"
        };
        options.Aggregations.Add(MetricAggregationType.Total);
        options.Aggregations.Add(MetricAggregationType.Average);
        options.Aggregations.Add(MetricAggregationType.Maximum);
        var response = await client.QueryResourceAsync(resourceId, metricNames, options, cancellationToken);
        return response.Value.Metrics.Select(metric => new RawAzureMetric(metric.Name, metric.Unit.ToString(),
            metric.TimeSeries.SelectMany(series => series.Values)
                .Select(point => new RawAzureMetricPoint(point.TimeStamp, point.Average, point.Maximum, point.Total)).ToArray())).ToArray();
    }
}

internal interface IAzureMonitorMetricsCollector
{
    Task<AzureMetricCollectionResult> CollectAsync(AzureMetricCollectionRequest request, CancellationToken cancellationToken);
}

internal sealed class AzureMonitorMetricsCollector(
    IAzureMetricsQueryClient client, IOptions<AzureMonitorOptions> options, TimeProvider timeProvider) : IAzureMonitorMetricsCollector
{
    internal static readonly IReadOnlyDictionary<AzureMetricProfile, IReadOnlyList<AzureMetricDefinition>> Profiles =
        new Dictionary<AzureMetricProfile, IReadOnlyList<AzureMetricDefinition>>
        {
            [AzureMetricProfile.AppServiceBasicHealth] =
            [
                new("Requests", HealthMetricName.Requests, HealthMetricAggregation.Total, "Count"),
                new("Http5xx", HealthMetricName.Http5xx, HealthMetricAggregation.Total, "Count"),
                new("Http4xx", HealthMetricName.Http4xx, HealthMetricAggregation.Total, "Count"),
                new("HttpResponseTime", HealthMetricName.AverageResponseTime, HealthMetricAggregation.Average, "Seconds")
            ],
            [AzureMetricProfile.AppServiceHttpHealth] =
            [
                new("Requests", HealthMetricName.Requests, HealthMetricAggregation.Total, "Count"),
                new("Http5xx", HealthMetricName.Http5xx, HealthMetricAggregation.Total, "Count"),
                new("Http4xx", HealthMetricName.Http4xx, HealthMetricAggregation.Total, "Count"),
                new("HttpResponseTime", HealthMetricName.AverageResponseTime, HealthMetricAggregation.Average, "Seconds")
            ],
            [AzureMetricProfile.AppServiceResourcePressure] =
            [
                new("CpuTime", HealthMetricName.CpuTime, HealthMetricAggregation.Total, "Seconds"),
                new("AverageMemoryWorkingSet", HealthMetricName.MemoryWorkingSetBytes, HealthMetricAggregation.Average, "Bytes")
            ]
        };

    public async Task<AzureMetricCollectionResult> CollectAsync(AzureMetricCollectionRequest request, CancellationToken cancellationToken)
    {
        var definitions = Profiles[request.MetricProfile];
        var duration = request.TimeWindow switch
        {
            AzureMetricTimeWindow.Last30Minutes => TimeSpan.FromMinutes(30),
            AzureMetricTimeWindow.Last2Hours => TimeSpan.FromHours(2),
            AzureMetricTimeWindow.Last24Hours => TimeSpan.FromHours(24),
            _ => throw new ArgumentOutOfRangeException(nameof(request.TimeWindow))
        };
        var end = timeProvider.GetUtcNow();
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var raw = await client.QueryAsync(request.Resource.ResourceId, definitions.Select(item => item.AzureName).ToArray(),
                end - duration, end, timeout.Token);
            var metrics = definitions.Select((definition, index) => CreateMetric(definition, raw, index + 1)).ToArray();
            timer.Stop();
            var collected = metrics.Count(item => item.Status == AzureMetricCollectionStatus.Success);
            var status = collected == metrics.Length ? AzureMetricCollectionStatus.Success :
                collected == 0 ? AzureMetricCollectionStatus.NoData : AzureMetricCollectionStatus.Partial;
            return new(new(ApplicationEvidenceSource.AzureMonitorMetrics, request.MetricProfile, request.TimeWindow,
                status, timer.ElapsedMilliseconds, definitions.Count, collected), metrics);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(request, definitions.Count, AzureMetricCollectionStatus.Timeout, timer.ElapsedMilliseconds);
        }
        catch (RequestFailedException error) when (error.Status is 401 or 403)
        {
            return Failure(request, definitions.Count, AzureMetricCollectionStatus.Unauthorized, timer.ElapsedMilliseconds);
        }
        catch (RequestFailedException)
        {
            return Failure(request, definitions.Count, AzureMetricCollectionStatus.Unavailable, timer.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return Failure(request, definitions.Count, AzureMetricCollectionStatus.Unavailable, timer.ElapsedMilliseconds);
        }
    }

    private static CollectedAzureMetric CreateMetric(AzureMetricDefinition definition, IReadOnlyList<RawAzureMetric> raw, int ordinal)
    {
        var source = raw.FirstOrDefault(item => item.Name.Equals(definition.AzureName, StringComparison.OrdinalIgnoreCase));
        var values = source?.Points.Select(point => definition.Aggregation switch
        {
            HealthMetricAggregation.Total => point.Total,
            HealthMetricAggregation.Average => point.Average,
            HealthMetricAggregation.Maximum => point.Maximum,
            _ => null
        }).OfType<double>().ToArray() ?? [];
        double? value = values.Length == 0 ? null : definition.Aggregation switch
        {
            HealthMetricAggregation.Total => values.Sum(),
            HealthMetricAggregation.Average => values.Average(),
            HealthMetricAggregation.Maximum => values.Max(),
            _ => null
        };
        DateTimeOffset? observedAt = source?.Points.Count > 0 ? source.Points.Max(item => item.Timestamp) : null;
        return new($"azuremetric-{ordinal:000}", definition.HealthName, definition.Aggregation, value,
            source?.Unit ?? definition.Unit, observedAt,
            value.HasValue ? AzureMetricCollectionStatus.Success : AzureMetricCollectionStatus.NoData);
    }

    private static AzureMetricCollectionResult Failure(AzureMetricCollectionRequest request, int count,
        AzureMetricCollectionStatus status, long elapsed) =>
        new(new(ApplicationEvidenceSource.AzureMonitorMetrics, request.MetricProfile, request.TimeWindow,
            status, elapsed, count, 0), []);
}
