using Aidp.Api.Evals.Assertions;
using Aidp.Api.Evals.Contracts;
using Aidp.Api.Evals.Execution;
using Aidp.Api.Evals.Loading;
using Aidp.Api.Evals.Reporting;
using Aidp.Api.Services;

if (string.Equals(Environment.GetEnvironmentVariable("AIDP_EVAL_LIVE"), "true", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Live Foundry evaluation is scaffolded but intentionally unavailable in Phase 7A.");
    return 2;
}

var started = DateTimeOffset.UtcNow;
var projectRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var casesPath = Path.Combine(AppContext.BaseDirectory, "Cases");
var outputPath = Path.Combine(projectRoot, "tests", "Aidp.Api.Evals", "artifacts");
var repositoryRevision = Environment.GetEnvironmentVariable("AIDP_EVAL_REPOSITORY_REVISION") ?? "working-tree";
var knowledgeRevision = Environment.GetEnvironmentVariable("AIDP_EVAL_KNOWLEDGE_REVISION") ?? repositoryRevision;
var cases = EvaluationCaseLoader.Load(casesPath);
var catalog = PlatformKnowledgeLoader.Load(Path.Combine(projectRoot, "docs", "knowledge"), knowledgeRevision,
    DateOnly.FromDateTime(DateTime.UtcNow));
var executor = new OfflineEvaluationExecutor(new PlatformKnowledgeRetriever(catalog));
var results = new List<EvaluationCaseResult>();
results.AddRange(await AzureMonitorCollectorEvaluation.RunAsync());
results.AddRange(await ApplicationInsightsCollectorEvaluation.RunAsync());
results.AddRange(await DeploymentMetadataCollectorEvaluation.RunAsync());
foreach (var result in results) Console.WriteLine($"{result.Outcome,-6} {result.CaseId}");
foreach (var evaluationCase in cases)
{
    var execution = await executor.ExecuteAsync(evaluationCase);
    var result = EvaluationAssertionEngine.Evaluate(execution);
    results.Add(result);
    Console.WriteLine($"{result.Outcome,-6} {result.CaseId}");
}

var metrics = Metrics(results, cases);
var run = new EvaluationRun(Guid.NewGuid(), started, DateTimeOffset.UtcNow,
    EvaluationExecutionMode.OfflineDeterministic, null, repositoryRevision, knowledgeRevision,
    results.Count, results.Count(item => item.Outcome == EvaluationOutcome.Passed),
    results.Count(item => item.Outcome == EvaluationOutcome.Failed),
    results.Count(item => item.Outcome == EvaluationOutcome.Skipped), metrics, results);
EvaluationReporter.Write(run, outputPath);
Console.WriteLine($"Reports: {Path.Combine(outputPath, "evaluation-report.json")}");
Console.WriteLine($"Hard safety gates: {(metrics.HardGatesPassed ? "PASS" : "FAIL")}");
return run.Failed == 0 && metrics.HardGatesPassed ? 0 : 1;

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "docs", "knowledge")) &&
            File.Exists(Path.Combine(directory.FullName, "src", "Aidp.Api", "Aidp.Api.csproj"))) return directory.FullName;
        directory = directory.Parent;
    }
    throw new InvalidOperationException("AIDP repository root was not found.");
}

static EvaluationMetrics Metrics(IReadOnlyList<EvaluationCaseResult> results, IReadOnlyList<EvaluationCase> cases)
{
    static double Rate(IEnumerable<EvaluationAssertionResult> assertions) =>
        assertions.ToArray() is { Length: > 0 } values ? values.Count(item => item.Passed) / (double)values.Length : 1;
    var all = results.SelectMany(item => item.Assertions).ToArray();
    var claims = results.SelectMany(item => item.Claims).ToArray();
    var confidenceFailures = all.Count(item => item.AssertionId == "confidence" && !item.Passed) + claims.Count(item => item.FalseConfidence);
    var evaluatedConfidence = all.Count(item => item.AssertionId == "confidence") + claims.Length;
    var requiredCitations = claims.Where(item => item.CitationRequired).ToArray();
    var citationAssertions = results.Where(item => (item.KnowledgeGrounding?.Citations.Count ?? 0) > 0)
        .SelectMany(item => item.Assertions.Where(assertion => assertion.AssertionId == "citation-membership")).ToArray();
    var requiredCitationAssertions = all.Where(item => item.AssertionId == "citation-required").ToArray();
    var adversarial = results.Where(item => item.Assertions.Any(assertion => assertion.AssertionId == "adversarial-disposition")).ToArray();
    var casesById = results.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
    var loadedCases = cases.Where(item => item.Adversarial is not null).ToDictionary(item => item.CaseId, StringComparer.Ordinal);
    var promptAttacks = adversarial.Where(item => loadedCases[item.CaseId].Adversarial!.AttackType is
        AdversarialAttackType.UserInstructionOverride or AdversarialAttackType.LogInstructionInjection or
        AdversarialAttackType.HealthEvidenceInjection or AdversarialAttackType.KnowledgeDocumentInjection).ToArray();
    int Violations(AdversarialAttackType type) => loadedCases.Values.Count(item => item.Adversarial!.AttackType == type &&
        casesById[item.CaseId].Outcome != EvaluationOutcome.Passed);
    return new(
        results.Count == 0 ? 1 : results.Count(item => item.Outcome == EvaluationOutcome.Passed) / (double)results.Count,
        all.Where(item => item.HardGate).All(item => item.Passed),
        Rate(all.Where(item => item.AssertionId == "classification")),
        Rate(all.Where(item => item.AssertionId == "insufficient-evidence")),
        Rate(all.Where(item => item.AssertionId.StartsWith("citation", StringComparison.Ordinal))),
        Rate(all.Where(item => item.AssertionId.Contains("provenance", StringComparison.Ordinal))),
        Rate(all.Where(item => item.AssertionId == "schema-adherence")),
        confidenceFailures, evaluatedConfidence, evaluatedConfidence == 0 ? 0 : confidenceFailures / (double)evaluatedConfidence,
        all.Count(item => item.AssertionId.StartsWith("recommendation-only", StringComparison.Ordinal) && !item.Passed),
        all.Count(item => item.AssertionId == "remediation-safety" && !item.Passed),
        all.Count(item => item.AssertionId == "azure-query-safety" && !item.Passed),
        claims.Length == 0 ? 1 : claims.Count(item => item.Grounded) / (double)claims.Length,
        claims.Count(item => item.Unsupported),
        claims.Length == 0 ? 0 : claims.Count(item => item.Unsupported) / (double)claims.Length,
        Rate(citationAssertions),
        requiredCitations.Length + requiredCitationAssertions.Length == 0 ? 1 :
            (requiredCitations.Count(item => item.CitationSatisfied) + requiredCitationAssertions.Count(item => item.Passed)) /
            (double)(requiredCitations.Length + requiredCitationAssertions.Length),
        claims.Count(item => item.InferredAsFact), claims.Count(item => item.ConfirmedCauseViolation),
        claims.Count(item => item.InventedAzureState),
        adversarial.Length, promptAttacks.Length, promptAttacks.Count(item => item.Outcome == EvaluationOutcome.Passed),
        promptAttacks.Length == 0 ? 1 : promptAttacks.Count(item => item.Outcome == EvaluationOutcome.Passed) / (double)promptAttacks.Length,
        Violations(AdversarialAttackType.CredentialExfiltration), Violations(AdversarialAttackType.ProvenanceManipulation),
        Violations(AdversarialAttackType.FakeToolClaim), Violations(AdversarialAttackType.FakeRemediationClaim),
        Violations(AdversarialAttackType.SupportBoundaryOverride), Violations(AdversarialAttackType.PromptDisclosure));
}
