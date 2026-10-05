using System.Text.Json;
using System.Text.Json.Serialization;
using Aidp.Api.Evals.Contracts;

namespace Aidp.Api.Evals.Loading;

internal static class EvaluationCaseLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    internal static IReadOnlyList<EvaluationCase> Load(string root)
    {
        if (!Directory.Exists(root)) throw new InvalidOperationException("Evaluation case directory was not found.");
        var cases = new List<EvaluationCase>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<List<EvaluationCase>>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidOperationException("Evaluation file did not contain a case array.");
                cases.AddRange(loaded);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Evaluation case file '{Path.GetFileName(path)}' is malformed.");
            }
        }

        foreach (var group in cases.GroupBy(item => item.CaseId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            throw new InvalidOperationException($"Duplicate evaluation case ID '{group.Key}'.");
        foreach (var item in cases) Validate(item);
        return cases.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();
    }

    private static void Validate(EvaluationCase item)
    {
        if (item.SchemaVersion is not (1 or 2 or 3)) throw Invalid(item, "unsupported schemaVersion");
        if (string.IsNullOrWhiteSpace(item.CaseId) || string.IsNullOrWhiteSpace(item.Title) ||
            string.IsNullOrWhiteSpace(item.Description) || string.IsNullOrWhiteSpace(item.ProviderFixture) ||
            string.IsNullOrWhiteSpace(item.KnowledgeMode)) throw Invalid(item, "required field missing");
        var matches = item.AssistantType switch
        {
            EvaluationAssistantType.InfrastructureReview => item.Input is InfrastructureReviewEvaluationInput,
            EvaluationAssistantType.DeploymentTroubleshooting => item.Input is TroubleshootingEvaluationInput,
            EvaluationAssistantType.ApplicationHealth => item.Input is HealthEvaluationInput,
            _ => false
        };
        if (!matches) throw Invalid(item, "assistantType does not match input payload");
        if (item.Tags is null || item.Expected is null || item.Expected.Evidence is null || item.Expected.Citations is null ||
            item.Expected.Safety is null || item.Expected.Knowledge is null) throw Invalid(item, "expected result is incomplete");
        if (item.KnowledgeMode is not ("catalog" or "none" or "conflict" or "stale" or "unsafeKnowledge"))
            throw Invalid(item, "knowledgeMode is invalid");
        if (item.Expected.ClaimExpectations?.GroupBy(claim => claim.ClaimId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1) == true)
            throw Invalid(item, "claim expectation IDs must be unique");
        if (item.SchemaVersion == 3 && item.Adversarial is null)
            throw Invalid(item, "adversarial expectation is required for schemaVersion 3");
        if (item.Adversarial is { ExpectedSemanticRule: { Length: > 0 } rule } &&
            !item.Expected.ExpectedSemanticRules.Contains(rule))
            throw Invalid(item, "adversarial semantic rule must be declared in expectedSemanticRules");
    }

    private static InvalidOperationException Invalid(EvaluationCase item, string reason) =>
        new($"Evaluation case '{item.CaseId}' is invalid: {reason}.");
}
