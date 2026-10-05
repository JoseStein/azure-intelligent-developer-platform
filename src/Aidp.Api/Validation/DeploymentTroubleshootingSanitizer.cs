using System.Text.RegularExpressions;
using Aidp.Api.Models;

namespace Aidp.Api.Validation;

internal static partial class DeploymentTroubleshootingSanitizer
{
    private const string Replacement = "[REDACTED_CREDENTIAL]";

    public static SanitizedDeploymentTroubleshootingInput Sanitize(CreateDeploymentTroubleshootingRequest request)
    {
        var redacted = false;
        string? Clean(string? value, int maximum)
        {
            if (value is null) return null;
            var cleaned = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            cleaned = ControlCharacterPattern().Replace(cleaned, string.Empty);
            foreach (var pattern in CredentialPatterns)
            {
                var replaced = pattern.Replace(cleaned, Replacement);
                redacted |= !string.Equals(cleaned, replaced, StringComparison.Ordinal);
                cleaned = replaced;
            }
            return cleaned.Length <= maximum ? cleaned : cleaned[..maximum];
        }

        var context = request.AzureContext is null ? null : new AzureDeploymentContext(
            Clean(request.AzureContext.Service, 100), Clean(request.AzureContext.ResourceType, 200),
            Clean(request.AzureContext.ResourceName, 260), Clean(request.AzureContext.ResourceGroup, 90),
            Clean(request.AzureContext.Region, 100));

        return new SanitizedDeploymentTroubleshootingInput(
            Clean(request.PipelineName, 200)!, request.RunId, Clean(request.FailedStage, 200), Clean(request.FailedJob, 200),
            Clean(request.FailedTask, 200), Clean(request.ErrorText, 12000)!, Clean(request.TerraformCommand, 500), context, redacted);
    }

    public static bool ContainsCredentialLikeMaterial(string value) =>
        CredentialPatterns.Any(pattern => pattern.IsMatch(value));

    public static bool ContainsCompletedActionClaim(string value)
    {
        foreach (Match match in CompletedActionPattern().Matches(value))
        {
            if (match.Groups["actor"].Success) return true;

            var clauseStart = match.Index - 1;
            while (clauseStart >= 0 && value[clauseStart] is not ('.' or '!' or '?' or '\n' or '"'))
                clauseStart--;
            var prefix = value[(clauseStart + 1)..match.Index];
            if (!AdvisoryOrNegativeContextPattern().IsMatch(prefix)) return true;
        }

        return false;
    }

    public static bool ContainsInventedIdentityOrRoleClaim(string value) =>
        InventedIdentityOrRolePattern().IsMatch(value);

    public static bool ContainsInventedToolClaim(string value) => InventedToolClaimPattern().IsMatch(value);

    public static bool ContainsPromptDisclosure(string value) => PromptDisclosurePattern().IsMatch(value);

    private static readonly Regex[] CredentialPatterns =
    [
        AuthorizationHeaderPattern(),
        JwtPattern(),
        SecretAssignmentPattern(),
        ConnectionStringPattern(),
        SasQueryPattern(),
        PrivateKeyPattern()
    ];

    [GeneratedRegex(@"(?i)\bAuthorization\s*:\s*Bearer\s+\S+", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"(?i)\b(?:client[_-]?secret|password|account[_-]?key|access[_-]?token|azure[_-]?devops[_-]?pat|pat)\b\s*[:=]\s*[^\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(@"(?i)\b(?:DefaultEndpointsProtocol|AccountName|AccountKey|SharedAccessSignature)=[^\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringPattern();

    [GeneratedRegex(@"(?i)(?:\?|&)(?:sv|sig|se|sp|sr|skoid|sktid|skt|ske|sks|skv)=[^\s&#]+", RegexOptions.CultureInvariant)]
    private static partial Regex SasQueryPattern();

    [GeneratedRegex(@"-----BEGIN [^-]*(?:PRIVATE KEY|CERTIFICATE)-----[\s\S]*?-----END [^-]*(?:PRIVATE KEY|CERTIFICATE)-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyPattern();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.CultureInvariant)]
    private static partial Regex ControlCharacterPattern();

    [GeneratedRegex(@"(?i)\b(?<actor>I|we|the assistant|the system)\s+(?:have\s+)?(?:fixed|changed|updated|deployed|rerun|reran|re-ran|applied|deleted|created|granted|revoked)\b|\b(?<passive>was|were|has been|have been)\s+(?:successfully\s+)?(?:fixed|changed|updated|deployed|rerun|reran|re-run|applied|deleted|created|granted|revoked)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompletedActionPattern();

    [GeneratedRegex(@"(?i)\b(?:should|could|may|might|must|consider|recommend|verify|check|determine|confirm|investigate|validate|ensure|whether|if|no|none|nothing|never)\b[^.!?""]{0,160}$", RegexOptions.CultureInvariant)]
    private static partial Regex AdvisoryOrNegativeContextPattern();

    [GeneratedRegex(@"(?i)\bthe\s+(?:service connection|managed identity|pipeline identity|required role|missing role)\s+is\s+[A-Za-z0-9_.@/-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InventedIdentityOrRolePattern();

    [GeneratedRegex(@"(?i)\b(?:I|we|the assistant|the system)\s+(?:queried|checked|inspected|read|retrieved)\s+(?:Azure(?: Monitor)?|Resource Health|Application Insights|Log Analytics|the Azure portal)\b", RegexOptions.CultureInvariant)]
    private static partial Regex InventedToolClaimPattern();

    [GeneratedRegex(@"(?i)\b(?:the\s+)?(?:system prompt|developer instructions?|hidden policy|internal (?:RAG|retrieved knowledge) context|model configuration secret)\s+(?:is|are|says?|contains?|:)\s*", RegexOptions.CultureInvariant)]
    private static partial Regex PromptDisclosurePattern();
}
