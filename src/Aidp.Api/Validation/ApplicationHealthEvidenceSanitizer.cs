using System.Text.RegularExpressions;
using Aidp.Api.Models;

namespace Aidp.Api.Validation;

internal static partial class ApplicationHealthEvidenceSanitizer
{
    private const string Credential = "[REDACTED_CREDENTIAL]";

    internal static SanitizedApplicationHealthInput Sanitize(CreateApplicationHealthAnalysisRequest request)
    {
        var redacted = false;
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Clean(string value)
        {
            var result = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            result = Controls().Replace(result, string.Empty);
            foreach (var pattern in Credentials)
            {
                var replaced = pattern.Replace(result, Credential);
                redacted |= !string.Equals(result, replaced, StringComparison.Ordinal);
                result = replaced;
            }
            result = UrlQuery().Replace(result, match => { redacted = true; return match.Groups["base"].Value; });
            result = Email().Replace(result, _ => { redacted = true; return "[REDACTED_USER]"; });
            result = ScopedGuid().Replace(result, match =>
            {
                redacted = true;
                var key = match.Groups["id"].Value;
                if (!aliases.TryGetValue(key, out var alias)) aliases[key] = alias = $"[RESOURCE_SCOPE_{aliases.Count + 1}]";
                return match.Groups["prefix"].Value + alias;
            });
            return result;
        }

        var metricNumber = 0;
        var azureMetricNumber = 0;
        var applicationInsightsNumber = 0;
        var evidence = request.Evidence.Select((item, index) =>
        {
            var trustedId = item.Type switch
            {
                ApplicationHealthEvidenceType.TrustedAzureMonitorMetric => $"azuremetric-{++azureMetricNumber:000}",
                ApplicationHealthEvidenceType.TrustedApplicationInsights => $"applicationinsights-{++applicationInsightsNumber:000}",
                ApplicationHealthEvidenceType.TrustedAzureResourceHealth => $"resourcehealth-{index + 1:000}",
                ApplicationHealthEvidenceType.TrustedDeploymentMetadata => $"deployment-{index + 1:000}",
                _ => $"evidence-{index + 1:000}"
            };
            return new SanitizedHealthEvidence(
                trustedId,
                item.Type, item.ObservedAt, Clean(item.Title), Clean(item.Content),
                (item.Metrics ?? []).Select(metric => new SanitizedHealthMetric(
                    item.Type is ApplicationHealthEvidenceType.TrustedAzureMonitorMetric or ApplicationHealthEvidenceType.TrustedApplicationInsights or ApplicationHealthEvidenceType.TrustedAzureResourceHealth or ApplicationHealthEvidenceType.TrustedDeploymentMetadata
                        ? trustedId : $"metric-{++metricNumber:000}",
                    metric.Name, metric.Aggregation, metric.Value, metric.Unit.Trim(), metric.ObservedAt)).ToList());
        }).ToList();
        string? Optional(string? value) => value is null ? null : Clean(value);
        var target = new ApplicationHealthTarget(Clean(request.Target.ApplicationName), Clean(request.Target.Environment),
            Optional(request.Target.AzureService), Optional(request.Target.ResourceName), Optional(request.Target.ResourceGroup), Optional(request.Target.Region));
        return new(Clean(request.Question), target, request.WindowStart, request.WindowEnd, evidence, redacted);
    }

    internal static bool ContainsCredentialLikeMaterial(string value) => Credentials.Any(x => x.IsMatch(value));
    internal static bool ContainsCompletedActionClaim(string value) =>
        DeploymentTroubleshootingSanitizer.ContainsCompletedActionClaim(value) || HealthCompletedAction().IsMatch(value);
    internal static bool ContainsInventedAzureQueryClaim(string value) =>
        InventedQueryClaim().IsMatch(value) && !TrustedEvidenceAttribution().IsMatch(value);

    private static readonly Regex[] Credentials =
    [Authorization(), Jwt(), Secret(), ApiKey(), ConnectionString(), Sas(), PrivateKey()];

    [GeneratedRegex(@"(?i)\bAuthorization\s*:\s*Bearer\s+\S+")] private static partial Regex Authorization();
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b")] private static partial Regex Jwt();
    [GeneratedRegex(@"(?i)\b(?:client[_-]?secret|password|account[_-]?key|access[_-]?token|pat)\b\s*[:=]\s*[^\s;,]+")] private static partial Regex Secret();
    [GeneratedRegex(@"(?i)\b(?:api[_-]?key|apikey)\b\s*[:=]\s*[^\s;,]+")] private static partial Regex ApiKey();
    [GeneratedRegex(@"(?i)\b(?:DefaultEndpointsProtocol|AccountName|AccountKey|SharedAccessSignature)=[^\r\n]+")] private static partial Regex ConnectionString();
    [GeneratedRegex(@"(?i)(?:\?|&)(?:sv|sig|se|sp|sr|skoid|sktid|skt|ske|sks|skv)=[^\s&#]+")] private static partial Regex Sas();
    [GeneratedRegex(@"-----BEGIN [^-]*(?:PRIVATE KEY|CERTIFICATE)-----[\s\S]*?-----END [^-]*(?:PRIVATE KEY|CERTIFICATE)-----", RegexOptions.IgnoreCase)] private static partial Regex PrivateKey();
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")] private static partial Regex Controls();
    [GeneratedRegex(@"(?<base>https?://[^\s?#]+)(?:\?[^\s#]*)", RegexOptions.IgnoreCase)] private static partial Regex UrlQuery();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)] private static partial Regex Email();
    [GeneratedRegex(@"(?<prefix>/(?:subscriptions|tenants)/|\b(?:subscriptionId|tenantId)\s*[:=]\s*)(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12})", RegexOptions.IgnoreCase)] private static partial Regex ScopedGuid();
    [GeneratedRegex(@"(?i)\b(?:I|we|the assistant|the system)\s+(?:queried|checked|retrieved|read|inspected)\s+(?:Azure|Application Insights|Log Analytics|Resource Health|the App Service)\b|\b(?:Azure|Application Insights|Log Analytics|Resource Health)\s+(?:shows|confirmed|returned)\b")] private static partial Regex InventedQueryClaim();
    [GeneratedRegex(@"(?i)\b(?:the\s+)?(?:trusted|collected|supplied)\s+(?:Azure\s+Resource\s+Health|Azure\s+Monitor|Application\s+Insights)\s+evidence\s+(?:reports|shows|indicates|confirmed|returned)\b")] private static partial Regex TrustedEvidenceAttribution();
    [GeneratedRegex(@"(?i)\b(?:I|we|the assistant|the system)\s+(?:have\s+)?(?:restarted|scaled|reconfigured|stopped|started)\b|\b(?:was|were|has been|have been)\s+(?:successfully\s+)?(?:restarted|scaled|reconfigured|stopped|started)\b")] private static partial Regex HealthCompletedAction();
}
