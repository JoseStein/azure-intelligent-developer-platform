using Aidp.Api.Models;

namespace Aidp.Api.Validation;

public static class ApplicationHealthAnalysisValidator
{
    internal const string TrustedContextReference = "AIDP trusted context";
    internal static readonly HashSet<string> TrustedPlatformFacts = new(StringComparer.Ordinal)
    {
        "AIDP health analysis is read-only and advisory.",
        "AIDP does not perform autonomous remediation."
    };

    private static readonly Dictionary<HealthMetricName, HashSet<string>> Units = new()
    {
        [HealthMetricName.Requests] = NewUnits("count"), [HealthMetricName.Http5xx] = NewUnits("count", "percent"),
        [HealthMetricName.FailedRequests] = NewUnits("count"),
        [HealthMetricName.Http4xx] = NewUnits("count", "percent"),
        [HealthMetricName.AverageResponseTime] = NewUnits("ms", "s"),
        [HealthMetricName.Dependencies] = NewUnits("count"),
        [HealthMetricName.CpuPercentage] = NewUnits("percent"),
        [HealthMetricName.CpuTime] = NewUnits("seconds", "s"),
        [HealthMetricName.MemoryWorkingSetBytes] = NewUnits("bytes", "kb", "mb", "gb"),
        [HealthMetricName.DependencyFailures] = NewUnits("count", "percent"),
        [HealthMetricName.AverageDependencyDuration] = NewUnits("ms"),
        [HealthMetricName.Availability] = NewUnits("percent")
    };

    public static Dictionary<string, string[]> Validate(CreateApplicationHealthAnalysisRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        Required(errors, nameof(request.Question), request.Question, 20, 1000);
        if (request.Target is null) errors[nameof(request.Target)] = ["Target is required."];
        else
        {
            Required(errors, "Target.ApplicationName", request.Target.ApplicationName, 3, 100);
            Required(errors, "Target.Environment", request.Target.Environment, 1, 50);
            Optional(errors, "Target.AzureService", request.Target.AzureService, 260);
            Optional(errors, "Target.ResourceName", request.Target.ResourceName, 260);
            Optional(errors, "Target.ResourceGroup", request.Target.ResourceGroup, 260);
            Optional(errors, "Target.Region", request.Target.Region, 260);
            if (!string.IsNullOrWhiteSpace(request.Target.AzureService) && request.Target.AzureService != "appservice")
                errors["Target.AzureService"] = ["AzureService must be exactly appservice when supplied."];
        }

        if (request.WindowStart.HasValue != request.WindowEnd.HasValue)
            errors[nameof(request.WindowStart)] = ["WindowStart and WindowEnd must be supplied together."];
        else if (request.WindowStart is { } start && request.WindowEnd is { } end &&
                 (start >= end || end - start > TimeSpan.FromDays(7)))
            errors[nameof(request.WindowEnd)] = ["The observation window must be positive and no longer than 7 days."];

        if (request.Evidence is null || request.Evidence.Count is < 1 or > 30)
            errors[nameof(request.Evidence)] = ["Evidence must contain between 1 and 30 items."];
        else
        {
            var combined = 0;
            var metrics = 0;
            for (var i = 0; i < request.Evidence.Count; i++)
            {
                var item = request.Evidence[i];
                if (item is null) { errors[$"Evidence[{i}]"] = ["Evidence item is required."]; continue; }
                if (!Enum.IsDefined(item.Type)) errors[$"Evidence[{i}].Type"] = ["Evidence type is invalid."];
                else if (item.Type is ApplicationHealthEvidenceType.TrustedAzureMonitorMetric or
                         ApplicationHealthEvidenceType.TrustedApplicationInsights or
                         ApplicationHealthEvidenceType.TrustedAzureResourceHealth or
                         ApplicationHealthEvidenceType.TrustedDeploymentMetadata)
                    errors[$"Evidence[{i}].Type"] = ["Trusted live evidence can only be created by the platform collector."];
                Required(errors, $"Evidence[{i}].Title", item.Title, 1, 200);
                Required(errors, $"Evidence[{i}].Content", item.Content, 10, 8000);
                combined += item.Content?.Length ?? 0;
                metrics += item.Metrics?.Count ?? 0;
                if (item.Metrics is null) continue;
                for (var j = 0; j < item.Metrics.Count; j++)
                {
                    var metric = item.Metrics[j];
                    if (metric is null) { errors[$"Evidence[{i}].Metrics[{j}]"] = ["Metric is required."]; continue; }
                    if (!Enum.IsDefined(metric.Name) || !Enum.IsDefined(metric.Aggregation))
                        errors[$"Evidence[{i}].Metrics[{j}]"] = ["Metric name and aggregation must be controlled values."];
                    else if (!double.IsFinite(metric.Value)) errors[$"Evidence[{i}].Metrics[{j}].Value"] = ["Metric value must be finite."];
                    else if (string.IsNullOrWhiteSpace(metric.Unit) || !Units[metric.Name].Contains(metric.Unit.Trim()))
                        errors[$"Evidence[{i}].Metrics[{j}].Unit"] = ["Metric unit is not valid for the selected metric."];
                }
            }
            if (combined > 40000) errors[nameof(request.Evidence)] = ["Combined evidence content must be 40000 characters or fewer."];
            if (metrics > 100) errors[nameof(request.Evidence)] = ["No more than 100 metric observations may be supplied."];
        }
        return errors;
    }

    internal static string? GetSemanticRule(ApplicationHealthModelOutput? output, SanitizedApplicationHealthInput input) =>
        GetSemanticRule(output, input, new(KnowledgeRetrievalStatus.NoRelevantKnowledge, [], [], []));

    internal static string? GetSemanticRule(ApplicationHealthModelOutput? output, SanitizedApplicationHealthInput input,
        KnowledgeRetrievalResult retrieval)
    {
        if (output is null || Invalid(output.Summary, 1500) || !Enum.IsDefined(output.OverallHealthAssessment) ||
            !List(output.Findings, 1, 10) || !List(output.MissingInformation, 0, 10) ||
            !List(output.RecommendedNextSteps, 1, 8) || !ValidConfidence(output.OverallConfidence) ||
            output.MissingInformation.Any(x => Invalid(x, 500)) || output.RecommendedNextSteps.Any(x => Invalid(x, 500)))
            return "outputLimitViolation";
        if (output.OverallHealthAssessment == OverallHealthAssessment.Unknown && output.OverallConfidence.Level != HealthConfidenceLevel.Low)
            return "unknownConfidenceMismatch";

        var allEvidence = output.Findings.SelectMany(f => f?.Evidence ?? []).ToList();
        var direct = allEvidence.Where(e => e is not null && IsDirect(e.Provenance)).ToList();
        if (output.OverallHealthAssessment == OverallHealthAssessment.Healthy && !direct.Any(e => IsAvailabilityReference(e!, input)))
            return "unsupportedHealthAssessment";
        if (output.OverallHealthAssessment == OverallHealthAssessment.Unhealthy && direct.Count == 0)
            return "unsupportedHealthAssessment";

        foreach (var finding in output.Findings)
        {
            if (finding is null || Invalid(finding.Finding, 1000) || Invalid(finding.RecommendedInvestigation, 1000) ||
                !Enum.IsDefined(finding.Category) || !Enum.IsDefined(finding.Severity) || !Enum.IsDefined(finding.ConclusionType) ||
                !ValidConfidence(finding.Confidence) || !List(finding.Evidence, 1, 8)) return "outputLimitViolation";
            foreach (var evidence in finding.Evidence)
            {
                var rule = EvidenceRule(evidence, input, retrieval);
                if (rule is not null) return rule;
            }
            var findingDirect = finding.Evidence.Where(e => IsDirect(e.Provenance)).ToList();
            if (finding.Severity == HealthSeverity.Critical && findingDirect.Count == 0) return "unsupportedHealthAssessment";
            if (finding.ConclusionType == HealthConclusionType.ConfirmedCause &&
                (finding.Confidence.Level != HealthConfidenceLevel.High || !HasExplicitCausalEvidence(findingDirect, input)))
                return "confirmedCauseInsufficientEvidence";
        }
        return null;
    }

    private static string? EvidenceRule(ApplicationHealthEvidence? evidence, SanitizedApplicationHealthInput input,
        KnowledgeRetrievalResult retrieval)
    {
        if (evidence is null || !Enum.IsDefined(evidence.Provenance) || Invalid(evidence.Statement, 1000) || evidence.SourceReference?.Length > 100)
            return "outputLimitViolation";
        if (evidence.Provenance == HealthEvidenceProvenance.ModelInference)
            return evidence.SourceReference is null ? null :
                retrieval.Chunks.Any(chunk => chunk.ChunkId == evidence.SourceReference)
                    ? "invalidKnowledgeProvenance" : "invalidModelInferenceSource";
        if (evidence.Provenance == HealthEvidenceProvenance.PlatformKnownFact)
            return evidence.SourceReference != TrustedContextReference ? "invalidEvidenceReference" :
                TrustedPlatformFacts.Contains(evidence.Statement) ? null : "invalidPlatformKnownFact";
        if (evidence.Provenance == HealthEvidenceProvenance.TrustedPlatformKnowledge)
            return evidence.SourceReference is not null && retrieval.Chunks.Any(chunk => chunk.ChunkId == evidence.SourceReference)
                ? null : evidence.SourceReference is not null && GetReferences(input).ContainsKey(evidence.SourceReference)
                    ? "invalidKnowledgeProvenance" : "invalidKnowledgeCitation";
        if (evidence.SourceReference is null) return "invalidEvidenceReference";
        if (retrieval.Chunks.Any(chunk => chunk.ChunkId == evidence.SourceReference))
            return "invalidKnowledgeProvenance";
        if (!GetReferences(input).TryGetValue(evidence.SourceReference, out var expected))
            return "invalidEvidenceReference";
        return expected == evidence.Provenance ? null : "invalidEvidenceProvenance";
    }

    internal static Dictionary<string, HealthEvidenceProvenance> GetReferences(SanitizedApplicationHealthInput input)
    {
        var result = new Dictionary<string, HealthEvidenceProvenance>(StringComparer.Ordinal);
        foreach (var item in input.Evidence)
        {
            result[item.Id] = Provenance(item.Type);
            foreach (var metric in item.Metrics) result[metric.Id] = item.Type switch
            {
                ApplicationHealthEvidenceType.TrustedAzureMonitorMetric => HealthEvidenceProvenance.TrustedAzureMonitorMetric,
                ApplicationHealthEvidenceType.TrustedApplicationInsights => HealthEvidenceProvenance.TrustedApplicationInsights,
                ApplicationHealthEvidenceType.TrustedAzureResourceHealth => HealthEvidenceProvenance.TrustedAzureResourceHealth,
                ApplicationHealthEvidenceType.TrustedDeploymentMetadata => HealthEvidenceProvenance.TrustedDeploymentMetadata,
                _ => HealthEvidenceProvenance.SuppliedAzureMetric
            };
        }
        return result;
    }

    internal static ApplicationHealthModelOutput ApplyTrustedEvidenceProvenance(
        ApplicationHealthModelOutput output, SanitizedApplicationHealthInput input,
        KnowledgeRetrievalResult retrieval)
    {
        var references = GetReferences(input);
        var findings = output.Findings.Select(finding => finding with
        {
            Evidence = finding.Evidence.Select(evidence => evidence with
            {
                Provenance = evidence.Provenance == HealthEvidenceProvenance.TrustedPlatformKnowledge ||
                    evidence.SourceReference is not null && retrieval.Chunks.Any(chunk => chunk.ChunkId == evidence.SourceReference)
                    ? evidence.Provenance : evidence.SourceReference switch
                {
                    null => HealthEvidenceProvenance.ModelInference,
                    TrustedContextReference => HealthEvidenceProvenance.PlatformKnownFact,
                    var reference when references.TryGetValue(reference, out var provenance) => provenance,
                    _ => evidence.Provenance
                }
            }).ToList()
        }).ToList();

        return output with { Findings = findings };
    }

    private static HealthEvidenceProvenance Provenance(ApplicationHealthEvidenceType type) => type switch
    {
        ApplicationHealthEvidenceType.AzureMetric => HealthEvidenceProvenance.SuppliedAzureMetric,
        ApplicationHealthEvidenceType.ApplicationInsights => HealthEvidenceProvenance.SuppliedApplicationInsights,
        ApplicationHealthEvidenceType.AppServiceLog => HealthEvidenceProvenance.SuppliedAppServiceLog,
        ApplicationHealthEvidenceType.DeploymentMetadata => HealthEvidenceProvenance.SuppliedDeploymentMetadata,
        ApplicationHealthEvidenceType.ResourceHealth => HealthEvidenceProvenance.SuppliedResourceHealth,
        ApplicationHealthEvidenceType.TrustedAzureMonitorMetric => HealthEvidenceProvenance.TrustedAzureMonitorMetric,
        ApplicationHealthEvidenceType.TrustedApplicationInsights => HealthEvidenceProvenance.TrustedApplicationInsights,
        ApplicationHealthEvidenceType.TrustedAzureResourceHealth => HealthEvidenceProvenance.TrustedAzureResourceHealth,
        ApplicationHealthEvidenceType.TrustedDeploymentMetadata => HealthEvidenceProvenance.TrustedDeploymentMetadata,
        _ => HealthEvidenceProvenance.SuppliedConfigurationMetadata
    };

    private static bool IsAvailabilityReference(ApplicationHealthEvidence evidence, SanitizedApplicationHealthInput input)
    {
        if (evidence.Provenance is HealthEvidenceProvenance.SuppliedResourceHealth or HealthEvidenceProvenance.TrustedAzureResourceHealth) return true;
        return evidence.SourceReference is { } reference && input.Evidence.SelectMany(x => x.Metrics)
            .Any(m => m.Id == reference && m.Name == HealthMetricName.Availability);
    }

    private static bool HasExplicitCausalEvidence(IReadOnlyList<ApplicationHealthEvidence> evidence, SanitizedApplicationHealthInput input) =>
        evidence.Any(e => e.SourceReference is { } id && input.Evidence.Any(item => item.Id == id &&
            (item.Type is ApplicationHealthEvidenceType.AppServiceLog or ApplicationHealthEvidenceType.ConfigurationMetadata or
                ApplicationHealthEvidenceType.ResourceHealth or ApplicationHealthEvidenceType.TrustedAzureResourceHealth ||
             item.Type == ApplicationHealthEvidenceType.ApplicationInsights &&
                item.Metrics.Any(metric => metric.Name == HealthMetricName.DependencyFailures))));

    private static bool IsDirect(HealthEvidenceProvenance provenance) => provenance is not
        (HealthEvidenceProvenance.ModelInference or HealthEvidenceProvenance.PlatformKnownFact or HealthEvidenceProvenance.TrustedPlatformKnowledge);
    private static bool ValidConfidence(HealthConfidence? confidence) => confidence is not null && Enum.IsDefined(confidence.Level) && !Invalid(confidence.Rationale, 750);
    private static bool List<T>(IReadOnlyList<T>? values, int min, int max) => values is not null && values.Count >= min && values.Count <= max;
    private static bool Invalid(string? value, int max) => string.IsNullOrWhiteSpace(value) || value.Length > max;
    private static HashSet<string> NewUnits(params string[] values) => new(values, StringComparer.Ordinal);
    private static void Required(Dictionary<string, string[]> errors, string name, string? value, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < min || value.Length > max || Controls(value))
            errors[name] = [$"{name} must be between {min} and {max} characters and contain no unsupported control characters."];
    }
    private static void Optional(Dictionary<string, string[]> errors, string name, string? value, int max)
    {
        if (value is not null && (value.Length > max || Controls(value))) errors[name] = [$"{name} must be {max} characters or fewer and contain no unsupported control characters."];
    }
    private static bool Controls(string value) => value.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'));
}
