namespace Penghou.Baize.Qualification.Execution;

// Suggested attribution classes are recorded alongside, never instead of, the
// raw assertion outcomes. A suggestion is a reading aid for the report; the
// observation stays authoritative and unresolved attribution stays unverified.
internal static class CompatibilityAttribution
{
    public const string ClassBaizeAdapterDefect = "baize-adapter-defect";
    public const string ClassProviderApiDifference = "provider-api-difference";
    public const string ClassModelCapabilityLimitation = "model-capability-limitation";
    public const string ClassConfigurationRequirement = "configuration-requirement";
    public const string ClassInfrastructureFailure = "infrastructure-failure";
    public const string ClassUnverified = "unverified";

    public static string FailureStage(AttemptOutcome outcome, string? failureCategory, string? executionCode)
    {
        if (executionCode is not null && (executionCode.StartsWith("http-dispatch-denied", StringComparison.Ordinal)
            || executionCode is "http-admission-without-attempt-lease" or "http-destination-substitution"
                or "http-concurrency-limit"))
            return "transport-admission";
        return failureCategory switch
        {
            "budget-denied" => "budget-admission",
            "provider-unavailable" or "rate-limited" or "authentication" or "authorization" => "transport-response",
            "timeout" or "caller-cancelled" or "timeout-or-cancellation" => "transport-lifecycle",
            "baize-protocol" => "protocol-decoding",
            _ => outcome == AttemptOutcome.Failed ? "assertion" : "unknown"
        };
    }
    public static string SuggestedClass(AttemptOutcome outcome, string? failureCategory, string? executionCode,
        bool knownDefectFixture)
    {
        if (knownDefectFixture) return ClassBaizeAdapterDefect;
        return failureCategory switch
        {
            "provider-unavailable" or "rate-limited" or "authentication" or "authorization"
                or "timeout" or "caller-cancelled" or "timeout-or-cancellation" => ClassInfrastructureFailure,
            "budget-denied" => ClassConfigurationRequirement,
            "baize-protocol" => ClassBaizeAdapterDefect,
            _ => outcome == AttemptOutcome.Failed ? ClassUnverified : ClassUnverified
        };
    }

    public static string Basis(bool knownDefectFixture) =>
        knownDefectFixture ? "deterministic-fixture" : "observed-normalized-output";

    // The preserved BQ-1a F1 reproduction: a co-terminal tool-call delta and
    // finish reason expose the existing OpenAI adapter ordering defect. The
    // signature is fixture-bound, never inferred from provider names.
    public static bool KnownF1DefectFixture(QualificationResultV3 result, CaseExecutionRequest request)
    {
        if (!request.RequiresNativeTools) return false;
        if (request.Subject.ProviderModel != RecordingTerminalHandler.CoTerminalMarker) return false;
        var streaming = result.Contracts.SingleOrDefault(c => c.ContractId == "chat.streaming-integrity@2");
        return streaming?.Assertions.Any(a => a.Id == "stream.sequence"
            && a.Status == AssertionStatus.Failed && a.Code == "invalid-event-sequence") == true;
    }
}
