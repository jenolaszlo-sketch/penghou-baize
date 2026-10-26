using System.Text.Json;
using Penghou.Baize.Qualification.Planning;

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
            var synthetic = TransportProfiles.Describe(transportProfile,
                requested.TryGetProperty("transportProfileRef", out var syntheticRef)
                    && syntheticRef.ValueKind == JsonValueKind.String ? syntheticRef.GetString() : null);
            return new EligibilityAssessmentEvidence("1.0", runId, selected.CaseId, selected.TrialId,
                selected.TrialSlot, selected.ModelId, selected.ScenarioId, StateSyntheticOnly,
                [], ["Synthetic execution exercises the real adapter composition but establishes no live-provider facts."],
                "QualificationAdmissionHandler -> RecordingTerminalHandler",
                synthetic.ProfileId, synthetic.Identity, null,
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

        // The transport profile is an exact, reviewed artifact. The plan's
        // complete recorded descriptor is re-hashed and compared with the
        // compiled profile and its runtime identity. Unknown, unreviewed or
        // substituted selections receive no physical-attempt bound. The
        // selected profile's bound is enforced by the guard and reserved by
        // the budget before credentials are resolved.
        var resolved = binding.GetProperty("resolved");
        TransportProfileSnapshot? recorded = null;
        if (resolved.TryGetProperty("transport", out var transport) && transport.ValueKind == JsonValueKind.Object)
        {
            try { recorded = transport.Deserialize<TransportProfileSnapshot>(CanonicalJson.Options); }
            catch (JsonException) { }
        }
        var requestRef = requested.TryGetProperty("transportProfileRef", out var requestRefElement)
            && requestRefElement.ValueKind == JsonValueKind.String ? requestRefElement.GetString() : null;
        var profileId = recorded?.ProfileId;
        var identity = recorded?.Identity;
        var bound = recorded?.PhysicalAttemptUpperBound;

        if (recorded is null || recorded.TransportClass != transportProfile)
        {
            critical.Add("transport-profile-substituted");
            critical.Add("wire-attempt-bound-unproven");
        }
        else
        {
            if (requestRef != recorded.ProfileId)
            {
                critical.Add("transport-profile-unreviewed");
                critical.Add("wire-attempt-bound-unproven");
            }
            if (!TransportProfiles.IsCurrent(recorded))
            {
                critical.Add("transport-profile-substituted");
                critical.Add("wire-attempt-bound-unproven");
            }
            if (!TransportProfiles.IsQualified(recorded))
            {
                critical.Add("transport-profile-unreviewed");
                critical.Add("wire-attempt-bound-unproven");
            }
            if (recorded.ProfileId == TransportProfiles.Http11ExactId
                && (recorded.ProtocolVersion != TransportProfiles.Http11Exact.ProtocolVersion
                    || recorded.ProtocolVersionPolicy != TransportProfiles.Http11Exact.ProtocolVersionPolicy))
            {
                critical.Add("http-protocol-policy-invalid");
                critical.Add("wire-attempt-bound-unproven");
            }
            if (recorded.ProfileId == TransportProfiles.Http11ExactId
                && bound != TransportProfiles.Http11Exact.PhysicalAttemptUpperBound)
                critical.Add("physical-attempt-bound-invalid");
        }
        if (transportProfile == "LoopbackDevelopment")
            critical.Add("loopback-not-live-eligible");
        if (critical.Count == 0)
            limitations.Add("wire-and-preallocation-byte-bounds-unproven");
        limitations.Add("provider-billing-semantics-not-established-by-transport-qualification");

        var bounded = recorded is not null
            && TransportProfiles.IsQualified(recorded)
            && TransportProfiles.IsCurrent(recorded)
            && bound == TransportProfiles.Http11Exact.PhysicalAttemptUpperBound
            && !critical.Contains("wire-attempt-bound-unproven", StringComparer.Ordinal);
        var chain = transportProfile == "LoopbackDevelopment"
            ? "QualificationAdmissionHandler -> SocketsHttpHandler(loopback, HTTP/1.1 exact, pinned)"
            : "QualificationAdmissionHandler -> SocketsHttpHandler(network, HTTP/1.1 exact, pinned)";
        var wire = bounded
            ? "at-most-1-physical-http-request-attempt-per-admitted-invocation"
            : "unknown-multiplicity";
        return new EligibilityAssessmentEvidence("1.0", runId, selected.CaseId, selected.TrialId,
            selected.TrialSlot, selected.ModelId, selected.ScenarioId,
            critical.Count == 0 ? StateEligible : StateBlocked,
            critical.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            limitations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            chain, profileId, identity, bound, wire, allowanceRef,
            reservation.ValueKind == JsonValueKind.Object ? NullableString(reservation, "sourceRef") : null,
            policyCost is null ? "unknown" : boundCost is null ? "unknown" : "bounded");
    }

    private static string? NullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
