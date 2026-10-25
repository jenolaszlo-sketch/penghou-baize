using System.Text.Json;

namespace Penghou.Baize.Qualification.Execution;

// Fail-closed live-eligibility assessment. Every reason is evidence-bound:
// unknown values never become permissive defaults, and an eligible result is
// never authorization. Eligibility is recorded as evidence per selected case;
// execution authority remains a separate explicit operator action.
internal static class LiveEligibility
{
    public const string StateEligible = "Eligible";
    public const string StateBlocked = "Blocked";
    public const string StateSyntheticOnly = "SyntheticOnly";

    public static EligibilityAssessmentEvidence Assess(FrozenRunPlan plan, RunCaseSelection selected, string runId)
    {
        var binding = selected.SubjectElement.GetProperty("binding");
        var requested = binding.GetProperty("requested");
        var transportProfile = requested.GetProperty("transportProfile").GetString();
        var policy = plan.Policy;
        var critical = new List<string>();
        var limitations = new List<string>();

        if (transportProfile == "SyntheticFake")
        {
            return new EligibilityAssessmentEvidence("1.0", runId, selected.CaseId, selected.TrialId,
                selected.TrialSlot, selected.ModelId, selected.ScenarioId, StateSyntheticOnly,
                [], ["Synthetic execution exercises the real adapter composition but establishes no live-provider facts."],
                "QualificationAdmissionHandler -> RecordingTerminalHandler",
                "not-applicable-synthetic", null, null, "not-required-synthetic");
        }

        // Real transport profiles: every required live guarantee must hold.
        if (requested.GetProperty("apiVersion").ValueKind == JsonValueKind.Null)
            critical.Add("protocol-revision-unknown");
        else
            limitations.Add("protocol-revision-evidence-unverified");

        var allowanceVerified = requested.GetProperty("allowanceVerified").GetBoolean();
        var allowanceRef = requested.TryGetProperty("allowanceRef", out var allowanceElement)
            && allowanceElement.ValueKind == JsonValueKind.String ? allowanceElement.GetString() : null;
        if (!allowanceVerified || allowanceRef is null)
            critical.Add("allowance-unverified");

        var secretSlot = requested.GetProperty("endpoint").TryGetProperty("apiKeySecretName", out var secret)
            && secret.ValueKind == JsonValueKind.String ? secret.GetString() : null;
        if (string.IsNullOrEmpty(secretSlot))
            critical.Add("credential-reference-missing");

        if (!policy.GetProperty("endpointAllowlist").EnumerateArray()
            .Any(e => e.GetString() == requested.GetProperty("endpoint").GetProperty("id").GetString()))
            critical.Add("endpoint-not-allowed");

        var reservation = selected.CaseElement.GetProperty("requestedReservation");
        if (reservation.ValueKind != JsonValueKind.Object
            || reservation.GetProperty("inputTokens").ValueKind != JsonValueKind.Number
            || reservation.GetProperty("outputTokens").ValueKind != JsonValueKind.Number
            || reservation.GetProperty("totalTokens").ValueKind != JsonValueKind.Number)
            critical.Add("token-reservation-bound-unknown");

        var policyCost = NullableString(policy, "maximumEstimatedCost");
        var boundCost = reservation.ValueKind == JsonValueKind.Object ? NullableString(reservation, "maximumCost") : null;
        var boundCurrency = reservation.ValueKind == JsonValueKind.Object ? NullableString(reservation, "currency") : null;
        var economicsRef = requested.TryGetProperty("economicsRef", out var economics)
            && economics.ValueKind == JsonValueKind.String ? economics.GetString() : null;
        if (economicsRef is not null)
        {
            // Paid access: a defensible cost bound is mandatory.
            if (policyCost is null)
                critical.Add("cost-envelope-unknown");
            else if (boundCost is null || boundCurrency is null)
                critical.Add("cost-reservation-bound-unknown");
            else if (boundCurrency != NullableString(policy, "currency"))
                critical.Add("cost-currency-mismatch");
        }
        else
        {
            limitations.Add("cost-envelope-unknown");
            limitations.Add("no-charge-profile-assumed-allowance-must-be-verified-trial");
        }

        // The last application boundary is guarded, but runtime-internal
        // replay below a real sockets terminal remains unobservable. Until a
        // separately reviewed lower-bound decision exists, no real profile
        // earns a hard wire-attempt guarantee.
        critical.Add("wire-attempt-bound-unproven");

        var chain = transportProfile == "LoopbackDevelopment"
            ? "QualificationAdmissionHandler -> SocketsHttpHandler(loopback)"
            : "QualificationAdmissionHandler -> SocketsHttpHandler(network)";
        var wire = transportProfile == "LoopbackDevelopment"
            ? "unknown-multiplicity-loopback" : "unknown-multiplicity-network";
        return new EligibilityAssessmentEvidence("1.0", runId, selected.CaseId, selected.TrialId,
            selected.TrialSlot, selected.ModelId, selected.ScenarioId,
            critical.Count == 0 ? StateEligible : StateBlocked,
            critical.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            limitations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            chain, wire, allowanceRef,
            reservation.ValueKind == JsonValueKind.Object ? NullableString(reservation, "sourceRef") : null,
            policyCost is null ? "unknown" : boundCost is null ? "unknown" : "bounded");
    }

    private static string? NullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
