using System.Text;
using System.Text.Json;
using Penghou.Baize.Qualification.Planning;
using Penghou.Siming;

namespace Penghou.Baize.Qualification.Execution;

// Builds the minimal offline report from recorded Siming evidence. It never
// conflates a completed workflow with a passed qualification: workflow status
// and contract outcome remain distinct fields.
internal sealed record ReportAttempt(
    string AttemptId,
    string? Outcome,
    bool ModelInvoked,
    string? DenialCode,
    string? FailureCategory,
    string? Attribution,
    string? FailureStage,
    string? SuggestedAttributionClass,
    int HttpDispatches,
    int? LastHttpStatus,
    string? UsageCompleteness,
    string? Settlement,
    bool Interrupted,
    bool Released,
    string[] FailedAssertionIds,
    EvidenceReference[] Evidence);

internal sealed record EvidenceReference(long Sequence, string EventType, string Hash);
internal sealed record ExecutionBoundaryDisclosure(int AuthorizationClaimCount, long BaizeDispatchCount,
    long ClientTransportAttemptCount, bool ClientTransportAttemptCountComplete, long ObservedHttpResponseCount,
    string[] ConfiguredProviderModels, string? ProviderReportedModelOrRoute, string ProviderExecutionCount);

internal sealed record ReportCase(
    string CaseId,
    string ModelId,
    string ScenarioId,
    string[] ObservedContractIds,
    string FixtureId,
    string FixtureHash,
    int TrialSlot,
    string TrialId,
    string SubjectId,
    string HostingProvider,
    string Provider,
    string ProviderModel,
    string EndpointId,
    string Protocol,
    string? ApiVersion,
    string TransportProfile,
    string? TransportProfileId,
    string? TransportProfileIdentity,
    int? PhysicalAttemptUpperBound,
    string? TransportChain,
    string? WireAttemptBound,
    IReadOnlyDictionary<string, string> Settings,
    bool NativeToolCalling,
    string? Outcome,
    string? WorkflowStatus,
    string? EligibilityState,
    string[] EligibilityCriticalReasons,
    string[] EligibilityLimitations,
    string[] FailedAssertionIds,
    string? FailureCategory,
    string? FailureStage,
    string? SuggestedAttributionClass,
    string? Attribution,
    bool Complete,
    string[] Limitations,
    ReportAttempt[] Attempts);

internal sealed record ReportRun(
    string RunId,
    string EvidenceCanonicalization,
    string PlanHash,
    string ContractRegistryHash,
    string InventoryHash,
    string PolicyHash,
    string SourceRevision,
    string Framework,
    bool LedgerVerified,
    bool CheckpointVerified,
    bool Complete,
    int Passed,
    int Failed,
    int Blocked,
    int Cancelled,
    int Inconclusive,
    int NotRun,
    long ConsumedModelCalls,
    long ConsumedHttpRequests,
    string? UnresolvedLiability,
    bool LiveAuthorized,
    string? LiveAuthorizationRef,
    string? LiveAuthorizationHash,
    string? LiveAuthorizationId,
    ExecutionBoundaryDisclosure ExecutionBoundary,
    ReportCase[] Cases);

internal static class ReportWriter
{
    public static ReportRun Write(string runDirectory, string runId, FrozenRunPlan plan, RunCaseSelection[] selection,
        IReadOnlyList<CaseOutcome> outcomes, LedgerVerificationResult verification, BudgetState budget,
        bool checkpointCaptured, ExecutionFaults faults, CancellationToken cancellationToken)
    {
        var ledger = new EvidenceLedger(Path.Combine(runDirectory, "evidence.db"), faults);
        var snapshot = new EvidenceSnapshot();
        ReadAll(ledger, snapshot, cancellationToken).GetAwaiter().GetResult();
        ledger.DisposeAsync().GetAwaiter().GetResult();
        var report = Build(plan, selection, outcomes, snapshot, verification, budget, checkpointCaptured, runId);
        var summary = new
        {
            kind = "qualification-report",
            schemaVersion = "1.0",
            report = report,
            generatedUtc = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };
        var reports = Path.Combine(runDirectory, "reports");
        Directory.CreateDirectory(reports);
        File.WriteAllBytes(Path.Combine(reports, "summary.json"), CanonicalJson.Write(summary));
        File.WriteAllBytes(Path.Combine(reports, "report.md"), Encoding.UTF8.GetBytes(ReportMarkdown.Build(report)));
        return report;
    }

    internal static ReportRun BuildReportForCommand(string runDirectory, string runId, FrozenRunPlan plan,
        RunCaseSelection[] selection, LedgerVerificationResult verification, bool checkpointCaptured,
        IReadOnlyDictionary<Guid, string> workflowStatuses, CancellationToken cancellationToken)
    {
        var ledger = new EvidenceLedger(Path.Combine(runDirectory, "evidence.db"), ExecutionFaults.None);
        var snapshot = new EvidenceSnapshot();
        ReadAll(ledger, snapshot, cancellationToken).GetAwaiter().GetResult();
        var budget = BudgetLedger.ComputeAsync(ledger, BudgetForPlan(plan), cancellationToken).GetAwaiter().GetResult();
        ledger.DisposeAsync().GetAwaiter().GetResult();
        var outcomes = selection.Select(s =>
        {
            var workflowStatus = workflowStatuses.TryGetValue(
                QualificationWorkflow.StableRunId(plan.PlanHash, s.CaseId, s.TrialId), out var status)
                ? status : "Unknown";
            var trialKey = TrialKey(s);
            if (!snapshot.Trials.TryGetValue(trialKey, out var trial))
                return new CaseOutcome(s.CaseId, s.TrialId, s.TrialSlot, s.ModelId, s.ScenarioId,
                    AttemptOutcome.NotRun, [], null, null, false, workflowStatus);
            var payload = trial;
            return new CaseOutcome(s.CaseId, s.TrialId, s.TrialSlot, s.ModelId, s.ScenarioId,
                ParseOutcome(payload.GetProperty("outcome").GetString()!),
                payload.GetProperty("failedAssertionIds").EnumerateArray().Select(a => a.GetString()!).ToArray(),
                NullableString(payload, "failureCategory"), NullableString(payload, "attribution"),
                payload.GetProperty("complete").GetBoolean(), workflowStatus);
        }).ToArray();
        return Build(plan, selection, outcomes, snapshot, verification, budget, checkpointCaptured, runId);
    }

    private static string TrialKey(RunCaseSelection selection) => $"{selection.CaseId}:{selection.TrialSlot}";

    private static ReportRun Build(FrozenRunPlan plan, RunCaseSelection[] selection, IReadOnlyList<CaseOutcome> outcomes,
        EvidenceSnapshot snapshot, LedgerVerificationResult verification, BudgetState budget, bool checkpointCaptured,
        string runId)
    {
        var source = plan.Document.GetProperty("source");
        var caseReports = selection.Select(selected =>
        {
            var outcome = outcomes.Single(o => o.CaseId == selected.CaseId);
            var trial = snapshot.Trials.TryGetValue(TrialKey(selected), out var trialPayload)
                ? trialPayload : (JsonElement?)null;
            var trialAttemptIds = trial is { } trialElement ? trialElement.GetProperty("attemptIds")
                .EnumerateArray().Select(a => a.GetString()!).ToHashSet(StringComparer.Ordinal) : [];
            var attempts = snapshot.Attempts.Where(id => SnapshotMatches(snapshot, id, selected))
                .Order(StringComparer.Ordinal).Select(id => BuildAttempt(snapshot, id, trialAttemptIds.Contains(id))).ToArray();
            var binding = selected.SubjectElement.GetProperty("binding");
            var resolved = binding.GetProperty("resolved");
            var requested = binding.GetProperty("requested");
            var scenario = selected.CaseElement.GetProperty("observations").EnumerateArray()
                .Single(o => o.GetProperty("id").GetString() == selected.ScenarioId);
            var limitations = new List<string>();
            foreach (var limitation in plan.Document.GetProperty("limitations").EnumerateArray())
                limitations.Add(limitation.GetString()!);
            if (outcome.Attribution is null && outcome.Outcome != AttemptOutcome.Passed)
                limitations.Add("Attribution remains unrefined; infrastructure and capability causes are not conflated.");
            var eligibility = snapshot.Eligibility.TryGetValue(TrialKey(selected), out var eligibilityPayload)
                ? eligibilityPayload : (JsonElement?)null;
            var eligibilityState = eligibility is { } e ? e.GetProperty("state").GetString() : null;
            var eligibilityCritical = eligibility is { } criticalElement ? criticalElement.GetProperty("criticalReasons")
                .EnumerateArray().Select(r => r.GetString()!).ToArray() : [];
            var eligibilityLimitations = eligibility is { } limitationElement ? limitationElement.GetProperty("limitations")
                .EnumerateArray().Select(r => r.GetString()!).ToArray() : [];
            var transportProfileId = eligibility is { } profileElement ? NullableString(profileElement, "transportProfileId") : null;
            var transportProfileIdentity = eligibility is { } identityElement ? NullableString(identityElement, "transportProfileIdentity") : null;
            var physicalAttemptUpperBound = eligibility is { } boundElement ? NullableInt(boundElement, "physicalAttemptUpperBound") : null;
            var transportChain = eligibility is { } chainElement ? NullableString(chainElement, "transportChain") : null;
            var wireAttemptBound = eligibility is { } wireElement ? NullableString(wireElement, "wireAttemptBound") : null;
            var failureStage = attempts.Select(a => a.FailureStage).FirstOrDefault(s => s is not null);
            var suggestedClass = attempts.Select(a => a.SuggestedAttributionClass).FirstOrDefault(s => s is not null);
            return new ReportCase(selected.CaseId, selected.ModelId, selected.ScenarioId,
                selected.CaseElement.GetProperty("observations").EnumerateArray().Select(o => o.GetProperty("id").GetString()!).ToArray(),
                scenario.GetProperty("fixtureId").GetString()!, scenario.GetProperty("fixtureHash").GetString()!,
                selected.TrialSlot, selected.TrialId, selected.SubjectId,
                requested.GetProperty("service").GetString()!,
                resolved.GetProperty("provider").GetString()!, resolved.GetProperty("providerModel").GetString()!,
                resolved.GetProperty("endpointId").GetString()!, requested.GetProperty("protocol").GetString()!,
                NullableString(requested, "apiVersion"), requested.GetProperty("transportProfile").GetString()!,
                transportProfileId, transportProfileIdentity, physicalAttemptUpperBound, transportChain, wireAttemptBound,
                resolved.GetProperty("settings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal),
                resolved.GetProperty("capabilities").GetProperty("nativeToolCalling").GetBoolean(),
                outcome.Outcome.ToString(),
                outcome.Status, eligibilityState, eligibilityCritical, eligibilityLimitations,
                outcome.FailedAssertionIds, outcome.FailureCategory, failureStage, suggestedClass, outcome.Attribution,
                outcome.Complete, limitations.ToArray(), attempts);
        }).ToArray();
        var summary = Summarize(outcomes);
        var liveAuthorizationRef = snapshot.RunStarted is { } runStarted
            ? NullableString(runStarted, "liveAuthorizationRef") : null;
        var liveAuthorizationHash = snapshot.RunStarted is { } runStartedHash
            ? NullableString(runStartedHash, "liveAuthorizationHash") : null;
        var liveAuthorizationId = snapshot.RunStarted is { } runStartedId
            ? NullableString(runStartedId, "liveAuthorizationId") : null;
        var admittedDispatchIds = snapshot.DispatchAdmissions;
        var clientDispatchIds = snapshot.DispatchResults.Values.SelectMany(values => values)
            .Select(value => value.DispatchId).ToHashSet(StringComparer.Ordinal);
        var observedResponseIds = snapshot.DispatchResults.Values.SelectMany(values => values)
            .Where(value => value.StatusCode is not null).Select(value => value.DispatchId)
            .ToHashSet(StringComparer.Ordinal);
        var boundary = new ExecutionBoundaryDisclosure(
            liveAuthorizationId is null ? 0 : 1,
            admittedDispatchIds.Count,
            clientDispatchIds.Count,
            clientDispatchIds.SetEquals(admittedDispatchIds),
            observedResponseIds.Count,
            caseReports.Select(c => c.ProviderModel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            null,
            "unknown");
        return new ReportRun(runId, CanonicalJson.EvidenceDigestVersion, plan.PlanHash, plan.ContractRegistryHash,
            source.GetProperty("inventoryHash").GetString()!, source.GetProperty("policyHash").GetString()!,
            plan.SourceRevision, plan.Framework, verification.IsValid, checkpointCaptured && verification.IsValid,
            summary.Complete && verification.IsValid, summary.Passed, summary.Failed, summary.Blocked,
            summary.Cancelled, summary.Inconclusive, summary.NotRun, budget.ConsumedModelCalls,
            budget.ConsumedHttpRequests, UnresolvedLiability(snapshot),
            liveAuthorizationRef is not null, liveAuthorizationRef, liveAuthorizationHash, liveAuthorizationId,
            boundary, caseReports);
    }

    private static string? UnresolvedLiability(EvidenceSnapshot snapshot)
    {
        var unresolved = snapshot.Attempts.Count(id => !snapshot.Settled.ContainsKey(id) && !snapshot.Released.ContainsKey(id)
            && (snapshot.Intents.ContainsKey(id) || snapshot.Finished.ContainsKey(id)));
        return unresolved == 0 ? null : "conservative-reservation-retained-for-unsettled-attempts";
    }

    private static bool SnapshotMatches(EvidenceSnapshot snapshot, string attemptId, RunCaseSelection selected)
    {
        JsonElement Read(Dictionary<string, JsonElement> map) => map.TryGetValue(attemptId, out var value) ? value : default;
        bool Matches(JsonElement payload) => payload.ValueKind == JsonValueKind.Object
            && payload.GetProperty("caseId").GetString() == selected.CaseId
            && payload.GetProperty("trialId").GetString() == selected.TrialId;
        return Matches(Read(snapshot.Reservations)) || Matches(Read(snapshot.Intents)) || Matches(Read(snapshot.Denied));
    }

    private static ReportAttempt BuildAttempt(EvidenceSnapshot snapshot, string attemptId, bool trialFinalized)
    {
        JsonElement Read(Dictionary<string, JsonElement> map) => map.TryGetValue(attemptId, out var value) ? value : default;
        var finished = Read(snapshot.Finished);
        var settle = Read(snapshot.Settled);
        var denied = Read(snapshot.Denied);
        var references = snapshot.Events.Where(e => e.AttemptId == attemptId)
            .Select(e => new EvidenceReference(e.Sequence, e.EventType, e.Hash)).ToArray();
        var outcome = finished.ValueKind == JsonValueKind.Object ? finished.GetProperty("outcome").GetString()
            : denied.ValueKind == JsonValueKind.Object ? "Blocked"
            : snapshot.Interrupted.ContainsKey(attemptId) ? "Inconclusive"
            : snapshot.Released.ContainsKey(attemptId) ? "NotRun" : null;
        var failed = finished.ValueKind == JsonValueKind.Object && finished.TryGetProperty("assertions", out var assertions)
            ? assertions.EnumerateArray().Where(a => a.GetProperty("status").GetString() == "Failed")
                .Select(a => a.GetProperty("id").GetString()!).ToArray() : [];
        _ = trialFinalized;
        var usageCompleteness = settle.ValueKind == JsonValueKind.Object && settle.TryGetProperty("usage", out var usage)
            && usage.ValueKind == JsonValueKind.Object ? NullableString(usage, "completeness") : null;
        var dispatches = snapshot.DispatchResults.TryGetValue(attemptId, out var dispatchList) ? dispatchList : [];
        var lastStatus = dispatches.LastOrDefault(value => value.StatusCode is not null)?.StatusCode;
        return new ReportAttempt(attemptId, outcome,
            finished.ValueKind == JsonValueKind.Object && finished.GetProperty("modelInvoked").GetBoolean(),
            denied.ValueKind == JsonValueKind.Object ? denied.GetProperty("reasonCode").GetString() : null,
            finished.ValueKind == JsonValueKind.Object ? NullableString(finished, "failureCategory") : null,
            finished.ValueKind == JsonValueKind.Object ? NullableString(finished, "attribution") : null,
            finished.ValueKind == JsonValueKind.Object ? NullableString(finished, "failureStage") : null,
            finished.ValueKind == JsonValueKind.Object ? NullableString(finished, "suggestedAttributionClass") : null,
            dispatches.Select(value => value.DispatchId).Distinct(StringComparer.Ordinal).Count(), lastStatus,
            usageCompleteness,
            settle.ValueKind == JsonValueKind.Object ? settle.GetProperty("settlement").GetString() : null,
            snapshot.Interrupted.ContainsKey(attemptId), snapshot.Released.ContainsKey(attemptId), failed, references);
    }

    private static AttemptOutcome ParseOutcome(string value) => Enum.TryParse<AttemptOutcome>(value, out var outcome) ? outcome : AttemptOutcome.Inconclusive;
    private static string? NullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static (int Passed, int Failed, int Blocked, int Cancelled, int Inconclusive, int NotRun, bool Complete)
        Summarize(IReadOnlyList<CaseOutcome> outcomes)
    {
        var summary = (Passed: 0, Failed: 0, Blocked: 0, Cancelled: 0, Inconclusive: 0, NotRun: 0);
        foreach (var outcome in outcomes)
        {
            switch (outcome.Outcome)
            {
                case AttemptOutcome.Passed: summary.Passed++; break;
                case AttemptOutcome.Failed: summary.Failed++; break;
                case AttemptOutcome.Blocked: summary.Blocked++; break;
                case AttemptOutcome.Cancelled: summary.Cancelled++; break;
                case AttemptOutcome.Inconclusive: summary.Inconclusive++; break;
                default: summary.NotRun++; break;
            }
        }
        return (summary.Passed, summary.Failed, summary.Blocked, summary.Cancelled, summary.Inconclusive, summary.NotRun,
            outcomes.Count > 0 && outcomes.All(o => o.Complete));
    }

    private static async Task ReadAll(EvidenceLedger ledger, EvidenceSnapshot snapshot, CancellationToken cancellationToken)
    {
        await foreach (var entry in ledger.ReadAllAsync(null, cancellationToken).ConfigureAwait(false))
        {
            snapshot.Events.Add(new AttemptEvent(entry.Sequence, entry.EventType, entry.Hash.ToString(), AttemptIdOf(entry)));
            if (entry.EventType == EvidenceLedger.AttemptReserved && TryPayload(entry, out var reservation))
                snapshot.Reservations[reservation.GetProperty("attemptId").GetString()!] = reservation;
            else if (entry.EventType == EvidenceLedger.DispatchIntent && TryPayload(entry, out var intent))
                snapshot.Intents[intent.GetProperty("attemptId").GetString()!] = intent;
            else if (entry.EventType == EvidenceLedger.AttemptFinished && TryPayload(entry, out var finished))
                snapshot.Finished[finished.GetProperty("attemptId").GetString()!] = finished;
            else if (entry.EventType == EvidenceLedger.UsageSettled && TryPayload(entry, out var settled))
                snapshot.Settled[settled.GetProperty("attemptId").GetString()!] = settled;
            else if (entry.EventType == EvidenceLedger.BudgetDenied && TryPayload(entry, out var denied))
                snapshot.Denied[denied.GetProperty("attemptId").GetString()!] = denied;
            else if (entry.EventType == EvidenceLedger.AttemptReleased && TryPayload(entry, out var released))
                snapshot.Released[released.GetProperty("attemptId").GetString()!] = released;
            else if (entry.EventType == EvidenceLedger.AttemptInterrupted && TryPayload(entry, out var interrupted))
                snapshot.Interrupted[interrupted.GetProperty("attemptId").GetString()!] = interrupted;
            else if (entry.EventType == EvidenceLedger.TrialFinished && TryPayload(entry, out var trial))
                snapshot.Trials[$"{trial.GetProperty("caseId").GetString()}:{trial.GetProperty("trialSlot").GetInt32()}"] = trial;
            else if (entry.EventType == EvidenceLedger.HttpDispatchResult && TryPayload(entry, out var dispatchResult))
            {
                var attemptId = dispatchResult.GetProperty("attemptId").GetString()!;
                if (!snapshot.DispatchResults.TryGetValue(attemptId, out var list))
                    snapshot.DispatchResults[attemptId] = list = [];
                list.Add(new DispatchResultRecord(
                    dispatchResult.GetProperty("dispatchId").GetString()!,
                    NullableInt(dispatchResult, "statusCode"),
                    dispatchResult.GetProperty("phase").GetString()!,
                    dispatchResult.GetProperty("bytesRead").GetInt64(),
                    dispatchResult.GetProperty("completed").GetBoolean()));
            }
            else if (entry.EventType == EvidenceLedger.HttpDispatchAdmitted && TryPayload(entry, out var admitted))
                snapshot.DispatchAdmissions.Add(admitted.GetProperty("dispatchId").GetString()!);
            else if (entry.EventType == EvidenceLedger.HttpDispatchDenied && TryPayload(entry, out var dispatchDenied))
            {
                var attemptId = dispatchDenied.GetProperty("attemptId").GetString()!;
                if (!snapshot.DispatchDenied.TryGetValue(attemptId, out var deniedList))
                    snapshot.DispatchDenied[attemptId] = deniedList = [];
                deniedList.Add(dispatchDenied.GetProperty("reasonCode").GetString()!);
            }
            else if (entry.EventType == EvidenceLedger.EligibilityAssessed && TryPayload(entry, out var eligibility))
                snapshot.Eligibility[$"{eligibility.GetProperty("caseId").GetString()}:{eligibility.GetProperty("trialSlot").GetInt32()}"] = eligibility;
            else if (entry.EventType == EvidenceLedger.RunFinished && TryPayload(entry, out var runFinished))
                snapshot.RunFinished = runFinished;
            else if (entry.EventType == EvidenceLedger.RunStarted && TryPayload(entry, out var runStarted))
                snapshot.RunStarted = runStarted;
        }
        snapshot.Attempts.UnionWith(snapshot.Reservations.Keys);
        snapshot.Attempts.UnionWith(snapshot.Intents.Keys);
        snapshot.Attempts.UnionWith(snapshot.Denied.Keys);
        snapshot.Attempts.UnionWith(snapshot.DispatchResults.Keys);
    }

    private static bool TryPayload(LedgerEntry entry, out JsonElement payload)
    {
        using var document = JsonDocument.Parse(entry.Payload);
        payload = document.RootElement.Clone();
        return payload.ValueKind == JsonValueKind.Object;
    }

    private static string? AttemptIdOf(LedgerEntry entry)
    {
        if (entry.EventType is EvidenceLedger.AttemptReserved or EvidenceLedger.DispatchIntent
            or EvidenceLedger.AttemptFinished or EvidenceLedger.UsageSettled or EvidenceLedger.BudgetDenied
            or EvidenceLedger.AttemptReleased or EvidenceLedger.AttemptInterrupted
            or EvidenceLedger.HttpDispatchAdmitted or EvidenceLedger.HttpDispatchResult
            or EvidenceLedger.HttpDispatchDenied)
        {
            using var document = JsonDocument.Parse(entry.Payload);
            return document.RootElement.TryGetProperty("attemptId", out var id) ? id.GetString() : null;
        }
        return null;
    }

    private static BudgetEnvelopeEvidence BudgetForPlan(FrozenRunPlan plan)
    {
        var policy = plan.Policy;
        return new BudgetEnvelopeEvidence(policy.GetProperty("maximumModelCalls").GetInt64(),
            policy.GetProperty("maximumHttpRequests").GetInt64(), NullableLong(policy, "maximumInputTokens"),
            NullableLong(policy, "maximumOutputTokens"), NullableLong(policy, "maximumReasoningTokens"),
            NullableLong(policy, "maximumTotalTokens"), NullableString(policy, "maximumEstimatedCost"),
            NullableString(policy, "currency"));
    }

    private static long? NullableLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;

    private static int? NullableInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    internal sealed record AttemptEvent(long Sequence, string EventType, string Hash, string? AttemptId);

    internal sealed record DispatchResultRecord(string DispatchId, int? StatusCode, string Phase, long BytesRead, bool Completed);

    internal sealed class EvidenceSnapshot
    {
        public List<AttemptEvent> Events { get; } = [];
        public Dictionary<string, JsonElement> Reservations { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Intents { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Finished { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Settled { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Denied { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Released { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Interrupted { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Trials { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<DispatchResultRecord>> DispatchResults { get; } = new(StringComparer.Ordinal);
        public HashSet<string> DispatchAdmissions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> DispatchDenied { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Eligibility { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Attempts { get; } = new(StringComparer.Ordinal);
        public JsonElement? RunFinished { get; set; }
        public JsonElement? RunStarted { get; set; }
    }
}
