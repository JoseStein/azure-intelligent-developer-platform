using Aidp.Api.Models;
using Microsoft.Extensions.Options;

namespace Aidp.Api.Services;

internal sealed class AzureMonitorOptions
{
    internal const string SectionName = "AzureMonitor";
    public int TimeoutSeconds { get; init; } = 15;
    public List<ApplicationResourceOptions> Applications { get; init; } = [];
}

internal sealed class ApplicationResourceOptions
{
    public string ApplicationName { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public string? ApplicationInsightsResourceId { get; init; }
    public string? ApplicationInsightsComponentName { get; init; }
    public string? LogAnalyticsWorkspaceId { get; init; }
}

internal interface IApplicationResourceCatalog
{
    bool TryResolve(string applicationName, out ApplicationResourceDescriptor descriptor);
}

internal sealed class ConfigurationApplicationResourceCatalog : IApplicationResourceCatalog
{
    private readonly IReadOnlyDictionary<string, ApplicationResourceDescriptor> resources;

    public ConfigurationApplicationResourceCatalog(IOptions<AzureMonitorOptions> options)
    {
        resources = options.Value.Applications.ToDictionary(item => item.ApplicationName,
            item => new ApplicationResourceDescriptor(item.ApplicationName, item.Environment, item.ResourceId,
                item.ApplicationInsightsResourceId, item.ApplicationInsightsComponentName,
                item.LogAnalyticsWorkspaceId,
                ApplicationResourceType.AppService, new HashSet<TrustedEvidenceSource>(
                    string.IsNullOrWhiteSpace(item.ApplicationInsightsResourceId)
                        ? [TrustedEvidenceSource.AzureMonitorMetrics, TrustedEvidenceSource.AzureResourceHealth]
                        : [TrustedEvidenceSource.AzureMonitorMetrics, TrustedEvidenceSource.ApplicationInsights, TrustedEvidenceSource.AzureResourceHealth])),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool TryResolve(string applicationName, out ApplicationResourceDescriptor descriptor) =>
        resources.TryGetValue(applicationName.Trim(), out descriptor!);
}
