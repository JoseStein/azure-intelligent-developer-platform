using Aidp.Api.Models;
using Aidp.Api.Services;

namespace Aidp.Api.Validation;

public static class AiInfrastructureReviewValidator
{
    public static Dictionary<string, string[]> Validate(CreateAiInfrastructureReviewRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Intent) || request.Intent.Trim().Length is < 20 or > 2000)
            errors[nameof(request.Intent)] = ["Intent must be between 20 and 2000 characters."];

        if (request.CurrentRequest is not null)
        {
            var current = request.CurrentRequest;
            var provisioningErrors = ApplicationRequestValidator.Validate(new CreateApplicationRequest(
                current.ResourceType, current.ApplicationName, current.Runtime, current.Environment, current.Description));
            foreach (var error in provisioningErrors)
                errors[$"CurrentRequest.{error.Key}"] = error.Value;
        }

        return errors;
    }

    internal static bool IsValid(AiInfrastructureReviewModelOutput? output)
    {
        if (output is null || Invalid(output.Summary, 2000) || Invalid(output.EvidenceReasoningSummary, 2000) ||
            output.Confidence is null || Invalid(output.Confidence.Rationale, 1000) ||
            !Enum.IsDefined(output.RiskLevel) || !Enum.IsDefined(output.Confidence.Level) ||
            !ValidList(output.RecommendedArchitecture, 1, 10) || !ValidList(output.SecurityFindings, 0, 10) ||
            !ValidList(output.ReliabilityFindings, 0, 10) || !ValidList(output.CostConsiderations, 0, 10) ||
            !ValidList(output.MissingInformation, 0, 10) || !ValidList(output.RecommendedNextSteps, 1, 10) ||
            output.RecommendedNextSteps.Any(step => Invalid(step, 500))) return false;

        if (output.RecommendedArchitecture.Any(item => item is null || Invalid(item.Component, 200) || Invalid(item.Purpose, 1000) ||
            Invalid(item.Recommendation, 1000) || !ValidEvidence(item.Evidence))) return false;

        return output.SecurityFindings.Concat(output.ReliabilityFindings).Concat(output.CostConsiderations)
            .Concat(output.MissingInformation).All(finding => finding is not null && !Invalid(finding.Finding, 1000) &&
                !Invalid(finding.Recommendation, 1000) && Enum.IsDefined(finding.Category) &&
                Enum.IsDefined(finding.Severity) && ValidEvidence(finding.Evidence));
    }

    internal static string? ValidateKnowledgeCitations(
        AiInfrastructureReviewModelOutput output, KnowledgeRetrievalResult retrieval)
    {
        foreach (var evidence in AllEvidence(output))
        {
            if (evidence.Type == AiEvidenceType.TrustedPlatformKnowledge)
            {
                if (KnowledgeCitationValidator.Validate(
                        new(KnowledgeCitationType.TrustedPlatformKnowledge, evidence.Statement, evidence.SourceReference), retrieval) is not null)
                    return "invalidKnowledgeCitation";
            }
            else if (evidence.Type == AiEvidenceType.ModelInference &&
                KnowledgeCitationValidator.Validate(
                    new(KnowledgeCitationType.ModelInference, evidence.Statement, evidence.SourceReference), retrieval) is not null)
                    return "invalidKnowledgeProvenance";
        }
        return null;
    }

    internal static IEnumerable<AiEvidence> AllEvidence(AiInfrastructureReviewModelOutput output) =>
        output.RecommendedArchitecture.SelectMany(item => item.Evidence)
            .Concat(output.SecurityFindings.SelectMany(item => item.Evidence))
            .Concat(output.ReliabilityFindings.SelectMany(item => item.Evidence))
            .Concat(output.CostConsiderations.SelectMany(item => item.Evidence))
            .Concat(output.MissingInformation.SelectMany(item => item.Evidence));

    private static bool ValidEvidence(IReadOnlyList<AiEvidence>? evidence) =>
        evidence is not null && ValidList(evidence, 1, 10) &&
        evidence.All(item => item is not null && !Invalid(item.Statement, 1000) &&
            Enum.IsDefined(item.Type) &&
            (item.SourceReference is null || item.SourceReference.Length <= 500));

    private static bool ValidList<T>(IReadOnlyList<T>? values, int minimum, int maximum) =>
        values is not null && values.Count >= minimum && values.Count <= maximum;

    private static bool Invalid(string? value, int maximum) => string.IsNullOrWhiteSpace(value) || value.Length > maximum;
}
