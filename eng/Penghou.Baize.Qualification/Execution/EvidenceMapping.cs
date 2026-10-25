using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Execution;

// Maps the contract's content-free result into evidence records. It never
// touches raw response text: only assertion ids/statuses/codes, counts and
// bounded diagnostics leave this mapping.
internal static class EvidenceMapping
{
    public static AssertionEvidence[] Assertions(QualificationResultV3 result, CaseExecutionRequest request)
    {
        // Assertion facts come from the accepted contract registry; the
        // request's observation list was verified at binding time. Observed
        // contracts (for example streaming-integrity) contribute their
        // assertion facts alongside the invocation scenario.
        var observed = new Dictionary<string, AssertionEvidence>(StringComparer.Ordinal);
        foreach (var contract in result.Contracts)
        {
            foreach (var assertion in contract.Assertions)
            {
                if (request.ObservedContractIds.Contains(contract.ContractId, StringComparer.Ordinal))
                    observed[assertion.Id] = new AssertionEvidence(assertion.Id, assertion.Status.ToString(), assertion.Code);
            }
        }
        var assertionIds = request.ObservedContractIds.SelectMany(observedId =>
            ContractRegistry.Find(observedId).AssertionIds).Distinct(StringComparer.Ordinal).ToArray();
        return assertionIds.Select(id => observed.TryGetValue(id, out var evidence)
            ? evidence : new AssertionEvidence(id, "NotObserved", "execution-incomplete")).ToArray();
    }

    public static AttemptOutcome Outcome(QualificationResultV3 result) => result.Succeeded ? AttemptOutcome.Passed
        : result.Contracts.Any(c => c.Disposition == ContractDisposition.Failed) ? AttemptOutcome.Failed
        : result.Contracts.Any(c => c.Disposition == ContractDisposition.Cancelled) ? AttemptOutcome.Cancelled
        : result.Contracts.Any(c => c.Disposition == ContractDisposition.Blocked) ? AttemptOutcome.Blocked
        : AttemptOutcome.Inconclusive;

    public static string? FailureCategory(QualificationResultV3 result)
    {
        var diagnostics = result.Diagnostics;
        return diagnostics.FailureCode switch
        {
            "provider-unavailable" => "provider-unavailable",
            "rate-limited" => "rate-limited",
            "authentication" => "authentication",
            "authorization" => "authorization",
            "timeout" => "timeout",
            "caller-cancelled" or "ambiguous-cancellation" => "timeout-or-cancellation",
            "request-validation" or "invalid-request" or "protocol-error" or "content-rejected" => "baize-protocol",
            "execution-error" => "inconclusive",
            _ => null
        };
    }

    public static string? Attribution(AttemptOutcome outcome, QualificationResultV3 result) => outcome switch
    {
        AttemptOutcome.Passed => null,
        AttemptOutcome.Failed => "contract-assertion-failure",
        AttemptOutcome.Blocked or AttemptOutcome.Cancelled or AttemptOutcome.Inconclusive => "not-attributed-infrastructure-or-incomplete",
        _ => null
    };

    public static string[] FailedAssertions(AssertionEvidence[] assertions) =>
        assertions.Where(a => a.Status == "Failed").Select(a => a.Id).ToArray();

    public static string Completeness(AssertionEvidence[] assertions) =>
        assertions.Any(a => a.Status == "NotObserved") ? "Incomplete" : "Complete";

    public static StreamDiagnosticsEvidence Diagnostics(QualificationResultV3 result) =>
        new(result.Diagnostics.InvocationCount, result.Diagnostics.EventCount, result.Diagnostics.ContentCharacters,
            result.Diagnostics.ReasoningCharacters, result.Diagnostics.ToolArgumentCharacters,
            result.Diagnostics.TerminalCount, result.Diagnostics.FinishReason.ToString(),
            result.Diagnostics.RouterAttemptCount, result.Diagnostics.ProviderDiagnosticsObserved,
            result.Diagnostics.FailureCode, result.Diagnostics.HttpStatusCode, result.Diagnostics.Integrity.Count);

    public static RouterEvidenceRecord Router(QualificationResultV3 result) =>
        new(result.Router.SnapshotCount, result.Router.AttemptCount, result.Router.SuccessfulAttempts,
            result.Router.FailedAttempts, result.Router.TerminalOrderValid);

    public static UsageEvidence Usage(QualificationResultV3 result)
    {
        var diagnostics = result.Diagnostics;
        var known = diagnostics.PromptTokens is not null && diagnostics.CompletionTokens is not null;
        return new UsageEvidence(diagnostics.PromptTokens, diagnostics.CompletionTokens, diagnostics.ThinkingTokens,
            null, known ? "Complete" : "Unknown", known ? "synthetic-terminal-transport" : "unavailable",
            null, "unknown");
    }
}
