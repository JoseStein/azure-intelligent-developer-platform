using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aidp.Api.Evals.Contracts;

namespace Aidp.Api.Evals.Reporting;

internal static class EvaluationReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) }
    };

    internal static void Write(EvaluationRun run, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "evaluation-report.json"), JsonSerializer.Serialize(run, JsonOptions));
        File.WriteAllText(Path.Combine(outputDirectory, "evaluation-summary.md"), Markdown(run));
    }

    private static string Markdown(EvaluationRun run)
    {
        var text = new StringBuilder();
        text.AppendLine("# AIDP Offline Evaluation Summary").AppendLine();
        text.AppendLine($"- Run mode: `{run.Mode}`");
        text.AppendLine($"- Cases: {run.CaseCount}");
        text.AppendLine($"- Passed: {run.Passed}");
        text.AppendLine($"- Failed: {run.Failed}");
        text.AppendLine($"- Skipped: {run.Skipped}");
        text.AppendLine($"- Hard safety gates: {(run.Metrics.HardGatesPassed ? "PASS" : "FAIL")}");
        text.AppendLine($"- Repository revision: `{run.RepositoryRevision}`");
        text.AppendLine($"- Knowledge revision: `{run.KnowledgeRevision}`").AppendLine();
        text.AppendLine("## Deterministic metrics").AppendLine();
        text.AppendLine("| Metric | Result |").AppendLine("|---|---:|");
        text.AppendLine($"| Total pass rate | {run.Metrics.TotalPassRate:P1} |");
        text.AppendLine($"| Classification accuracy | {run.Metrics.ClassificationAccuracy:P1} |");
        text.AppendLine($"| Insufficient-evidence correctness | {run.Metrics.InsufficientEvidenceCorrectness:P1} |");
        text.AppendLine($"| Citation validity | {run.Metrics.CitationValidityRate:P1} |");
        text.AppendLine($"| Provenance correctness | {run.Metrics.ProvenanceCorrectness:P1} |");
        text.AppendLine($"| Schema adherence | {run.Metrics.SchemaAdherence:P1} |");
        text.AppendLine($"| Structured grounded claim rate | {run.Metrics.StructuredGroundedClaimRate:P1} |");
        text.AppendLine($"| Unsupported structured claim rate | {run.Metrics.UnsupportedStructuredClaimRate:P1} |");
        text.AppendLine($"| Citation precision | {run.Metrics.CitationPrecision:P1} |");
        text.AppendLine($"| Citation recall | {run.Metrics.CitationRecall:P1} |");
        text.AppendLine($"| False-confidence rate | {run.Metrics.FalseHighConfidenceRate:P1} |");
        text.AppendLine($"| Evaluated confidence cases | {run.Metrics.EvaluatedConfidenceCases} |");
        text.AppendLine($"| Inferred-as-fact violations | {run.Metrics.InferredAsFactViolationCount} |");
        text.AppendLine($"| Confirmed-cause violations | {run.Metrics.ConfirmedCauseViolationCount} |");
        text.AppendLine($"| Invented Azure-state claims | {run.Metrics.InventedAzureStateClaimCount} |").AppendLine();
        text.AppendLine("## Controlled adversarial metrics").AppendLine();
        text.AppendLine($"- Adversarial cases: {run.Metrics.AdversarialCaseCount}");
        text.AppendLine($"- Prompt-injection cases blocked: {run.Metrics.PromptInjectionBlocked}/{run.Metrics.PromptInjectionCases} ({run.Metrics.PromptInjectionBlockRate:P1})");
        text.AppendLine($"- Credential-exfiltration violations: {run.Metrics.CredentialExfiltrationViolations}");
        text.AppendLine($"- Provenance-manipulation violations: {run.Metrics.ProvenanceManipulationViolations}");
        text.AppendLine($"- Fake tool-claim violations: {run.Metrics.FakeToolClaimViolations}");
        text.AppendLine($"- Fake remediation-claim violations: {run.Metrics.FakeRemediationClaimViolations}");
        text.AppendLine($"- Support-boundary violations: {run.Metrics.SupportBoundaryViolations}");
        text.AppendLine($"- Prompt-disclosure violations: {run.Metrics.PromptDisclosureViolations}").AppendLine();
        text.AppendLine("## Results by assistant").AppendLine();
        foreach (var group in run.Cases.GroupBy(item => item.AssistantType))
            text.AppendLine($"- {group.Key}: {group.Count(item => item.Outcome == EvaluationOutcome.Passed)}/{group.Count()} passed");
        var failures = run.Cases.Where(item => item.Outcome == EvaluationOutcome.Failed).ToArray();
        text.AppendLine().AppendLine("## Failed assertions").AppendLine();
        if (failures.Length == 0) text.AppendLine("No failed assertions.");
        foreach (var failure in failures)
        {
            text.AppendLine($"### {failure.CaseId}");
            foreach (var assertion in failure.Assertions.Where(item => !item.Passed))
                text.AppendLine($"- `{assertion.AssertionId}`: {assertion.SafeMessage}");
        }
        text.AppendLine().AppendLine("## Known limitations").AppendLine();
        text.AppendLine("- The evaluator does not claim a complete natural-language hallucination rate.");
        text.AppendLine("- Groundedness metrics in Phase 7B apply to structured claims and controlled assertion scenarios, not every natural-language sentence.");
        text.AppendLine("- Live Foundry evaluation is disabled by default and was not executed for this report.");
        text.AppendLine("- Inputs are synthetic and contain no production Azure identifiers or credentials.");
        text.AppendLine("- These tests demonstrate resistance to the controlled adversarial scenarios in this suite; they do not prove immunity to all prompt injection attacks.");
        return text.ToString();
    }
}
