using Aidp.Api.Models;

namespace Aidp.Api.Validation;

public static class DeploymentTroubleshootingValidator
{
    internal const string TrustedContextReference = "AIDP trusted context";
    internal static readonly HashSet<string> TrustedPlatformFacts = new(StringComparer.Ordinal)
    {
        "AIDP deployments use Azure DevOps pipelines.",
        "AIDP infrastructure deployments use Terraform.",
        "AIDP requires human approval before Terraform Apply."
    };

    public static Dictionary<string, string[]> Validate(CreateDeploymentTroubleshootingRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequired(errors, nameof(request.PipelineName), request.PipelineName, 200);
        if (request.RunId <= 0) errors[nameof(request.RunId)] = ["RunId must be greater than zero."];
        AddRequired(errors, nameof(request.ErrorText), request.ErrorText, 12000, 10);
        AddOptional(errors, nameof(request.FailedStage), request.FailedStage, 200);
        AddOptional(errors, nameof(request.FailedJob), request.FailedJob, 200);
        AddOptional(errors, nameof(request.FailedTask), request.FailedTask, 200);
        AddOptional(errors, nameof(request.TerraformCommand), request.TerraformCommand, 500);
        if (request.AzureContext is { } context)
        {
            AddOptional(errors, "AzureContext.Service", context.Service, 100);
            AddOptional(errors, "AzureContext.ResourceType", context.ResourceType, 200);
            AddOptional(errors, "AzureContext.ResourceName", context.ResourceName, 260);
            AddOptional(errors, "AzureContext.ResourceGroup", context.ResourceGroup, 90);
            AddOptional(errors, "AzureContext.Region", context.Region, 100);
        }
        return errors;
    }

    internal static bool IsValid(
        DeploymentTroubleshootingModelOutput? output, SanitizedDeploymentTroubleshootingInput input) =>
        GetSemanticRule(output, input, new(KnowledgeRetrievalStatus.NoRelevantKnowledge, [], [], [])) is null;

    internal static string? GetSemanticRule(
        DeploymentTroubleshootingModelOutput? output, SanitizedDeploymentTroubleshootingInput input,
        KnowledgeRetrievalResult retrieval)
    {
        if (output is null || Invalid(output.Summary, 1500) || !Enum.IsDefined(output.FailureCategory) ||
            !ValidList(output.Findings, 1, 8) || !ValidList(output.MissingInformation, 0, 8) ||
            !ValidList(output.RecommendedNextSteps, 1, 8) || output.OverallConfidence is null ||
            !ValidConfidence(output.OverallConfidence) ||
            output.MissingInformation.Any(item => Invalid(item, 500)) ||
            output.RecommendedNextSteps.Any(item => Invalid(item, 500))) return "outputLimitViolation";

        if (output.FailureCategory == DeploymentFailureCategory.Unknown &&
            output.OverallConfidence.Level != TroubleshootingConfidenceLevel.Low)
            return "unknownConfidenceMismatch";

        foreach (var finding in output.Findings)
        {
            if (finding is null || Invalid(finding.Finding, 1000) ||
                Invalid(finding.RecommendedInvestigation, 1000) || !ValidConfidence(finding.Confidence) ||
                !ValidList(finding.Evidence, 1, 8)) return "outputLimitViolation";

            foreach (var evidence in finding.Evidence)
            {
                var rule = GetEvidenceSemanticRule(evidence, input, retrieval);
                if (rule is not null) return rule;
            }

            if (finding.Confidence.Level == TroubleshootingConfidenceLevel.High &&
                !HasSpecificFailureSignal(input)) return "unsupportedHighConfidence";
        }

        if (output.OverallConfidence.Level == TroubleshootingConfidenceLevel.High &&
            !HasSpecificFailureSignal(input)) return "unsupportedHighConfidence";

        return null;
    }

    private static string? GetEvidenceSemanticRule(
        TroubleshootingEvidence? evidence, SanitizedDeploymentTroubleshootingInput input,
        KnowledgeRetrievalResult retrieval)
    {
        if (evidence is null || !Enum.IsDefined(evidence.Type) || Invalid(evidence.Statement, 1000) ||
            evidence.SourceReference?.Length > 300) return "outputLimitViolation";
        return evidence.Type switch
        {
            TroubleshootingEvidenceType.SuppliedPipelineEvidence =>
                evidence.SourceReference is not null && GetSuppliedReferences(input).Contains(evidence.SourceReference)
                    ? null : "invalidSuppliedEvidenceReference",
            TroubleshootingEvidenceType.PlatformKnownFact =>
                evidence.SourceReference != TrustedContextReference
                    ? "invalidPlatformKnownFactSource"
                    : TrustedPlatformFacts.Contains(evidence.Statement) ? null : "invalidPlatformKnownFact",
            TroubleshootingEvidenceType.TrustedPlatformKnowledge =>
                evidence.SourceReference is not null && retrieval.Chunks.Any(chunk => chunk.ChunkId == evidence.SourceReference)
                    ? null : evidence.SourceReference is not null && GetSuppliedReferences(input).Contains(evidence.SourceReference)
                        ? "invalidKnowledgeProvenance" : "invalidKnowledgeCitation",
            TroubleshootingEvidenceType.ModelInference =>
                evidence.SourceReference is null ? null :
                    retrieval.Chunks.Any(chunk => chunk.ChunkId == evidence.SourceReference)
                        ? "invalidKnowledgeProvenance" : "invalidModelInferenceSource",
            _ => "outputLimitViolation"
        };
    }

    private static bool HasSpecificFailureSignal(SanitizedDeploymentTroubleshootingInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.FailedStage) || !string.IsNullOrWhiteSpace(input.FailedJob) ||
            !string.IsNullOrWhiteSpace(input.FailedTask) || !string.IsNullOrWhiteSpace(input.TerraformCommand) ||
            input.AzureContext is not null) return true;
        var error = input.ErrorText;
        return error.Contains("AuthorizationFailed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("AuthenticationFailed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("roleAssignments/write", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(error, @"\b(?:40[013]|429|5\d\d)\b");
    }

    internal static HashSet<string> GetSuppliedReferences(SanitizedDeploymentTroubleshootingInput input)
    {
        var references = new HashSet<string>(StringComparer.Ordinal) { "pipelineName", "runId", "errorText" };
        AddIfSupplied(references, "failedStage", input.FailedStage);
        AddIfSupplied(references, "failedJob", input.FailedJob);
        AddIfSupplied(references, "failedTask", input.FailedTask);
        AddIfSupplied(references, "terraformCommand", input.TerraformCommand);
        if (input.AzureContext is { } context)
        {
            AddIfSupplied(references, "azureContext.service", context.Service);
            AddIfSupplied(references, "azureContext.resourceType", context.ResourceType);
            AddIfSupplied(references, "azureContext.resourceName", context.ResourceName);
            AddIfSupplied(references, "azureContext.resourceGroup", context.ResourceGroup);
            AddIfSupplied(references, "azureContext.region", context.Region);
        }
        return references;
    }

    private static void AddIfSupplied(HashSet<string> references, string identifier, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) references.Add(identifier);
    }

    private static bool ValidConfidence(TroubleshootingConfidence? confidence) =>
        confidence is not null && Enum.IsDefined(confidence.Level) && !Invalid(confidence.Rationale, 750);

    private static void AddRequired(Dictionary<string, string[]> errors, string name, string? value, int maximum, int minimum = 1)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < minimum)
            errors[name] = [$"{name} is required and must be at least {minimum} characters."];
        else if (value.Length > maximum || HasDisallowedControl(value))
            errors[name] = [$"{name} must be {maximum} characters or fewer and contain no unsupported control characters."];
    }

    private static void AddOptional(Dictionary<string, string[]> errors, string name, string? value, int maximum)
    {
        if (value is not null && (value.Length > maximum || HasDisallowedControl(value)))
            errors[name] = [$"{name} must be {maximum} characters or fewer and contain no unsupported control characters."];
    }

    private static bool HasDisallowedControl(string value) =>
        value.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n'));

    private static bool ValidList<T>(IReadOnlyList<T>? values, int minimum, int maximum) =>
        values is not null && values.Count >= minimum && values.Count <= maximum;

    private static bool Invalid(string? value, int maximum) => string.IsNullOrWhiteSpace(value) || value.Length > maximum;
}
