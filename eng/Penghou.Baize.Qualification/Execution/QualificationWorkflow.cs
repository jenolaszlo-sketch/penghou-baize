using Penghou.Baize.Qualification.Planning;
using Penghou.Zhinu;

namespace Penghou.Baize.Qualification.Execution;

// Invokes one qualification attempt through the routed Baize composition.
internal interface ICaseInvoker
{
    Task<QualificationResultV3> InvokeAsync(CaseExecutionRequest request, CancellationToken cancellationToken,
        CancellationToken deadlineToken);
}

internal sealed record WorkflowServices(
    EvidenceLedger Ledger,
    ICaseInvoker Invoker,
    ExecutionFaults Faults,
    TimeProvider Time);

internal sealed record BindingInput(CaseExecutionRequest Request, Guid WorkflowRunId);

internal sealed record BoundAttemptInput(BindingResult Binding, CaseExecutionRequest Request);

internal sealed record SettledAttemptInput(AttemptRecord Attempt, CaseExecutionRequest Request);

// The durable per-case qualification workflow. Step boundaries are chosen so
// that interrupted work is either replayable without another model invocation
// (bind, settle, finalize) or produces a distinct, separately reserved and
// recorded attempt (invoke-attempt). Zhinu owns the lifecycle; Siming owns the
// evidence; this workflow owns only the qualification composition.
internal sealed class QualificationWorkflow : IWorkflow<CaseExecutionRequest, TrialOutcomeRecord>
{
    public const string Name = "bq1b-qualification-case";
    public const string Version = "1";

    private readonly WorkflowServices _services;

    public QualificationWorkflow(WorkflowServices services)
    {
        _services = services;
    }

    // Wired once after the engine exists; never mutated afterwards.
    public Func<Guid, CancellationToken, Task<WorkflowRun?>> Probe { get; set; } =
        (_, _) => Task.FromResult<WorkflowRun?>(null);

    public async Task<TrialOutcomeRecord> RunAsync(WorkflowContext workflow, CaseExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var binding = await workflow.StepAsync("bind", new BindingInput(request, workflow.WorkflowRunId),
            static (input, ct) => Task.FromResult(BindingCheck.Run(input, ct)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Backstop only: the attempt's own deadline (inside the step) is the
        // policy limit; Zhinu's step timeout is a wider safety net.
        var backstop = TimeSpan.Parse(request.Deadlines.AttemptTimeout, System.Globalization.CultureInfo.InvariantCulture)
            + TimeSpan.Parse(request.Deadlines.MaximumDuration, System.Globalization.CultureInfo.InvariantCulture)
            + TimeSpan.FromMinutes(1);

        var attempt = await workflow.StepAsync("invoke-attempt", new BoundAttemptInput(binding, request),
            (input, ct) => InvokeAsync(input, ct),
            new StepOptions { ExecutionTimeout = backstop },
            cancellationToken).ConfigureAwait(false);

        var settled = await workflow.StepAsync("settle-usage", new SettledAttemptInput(attempt, request),
            (input, ct) => SettleAsync(input, ct),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await workflow.StepAsync("finalize-trial", settled,
            (input, ct) => FinalizeAsync(input, ct),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<AttemptRecord> InvokeAsync(BoundAttemptInput input, CancellationToken cancellationToken)
    {
        var request = input.Request;
        var attemptId = Guid.NewGuid().ToString("N");
        var units = BudgetLedger.UnitsFor(request);

        _services.Faults.BeforeReservationAppend?.Invoke(attemptId);
        var reservation = await BudgetLedger.ReserveAsync(_services.Ledger, request, attemptId, units,
            cancellationToken).ConfigureAwait(false);
        if (reservation.DenialCode is not null)
        {
            await _services.Ledger.AppendAsync(EvidenceLedger.BudgetStream, EvidenceLedger.BudgetDenied,
                new BudgetDeniedEvidence("1.0", attemptId, request.RunId, request.CaseId, request.TrialId, request.TrialSlot,
                    request.ModelId, request.ScenarioId, reservation.DenialCode),
                idempotencyKey: DeniedKey(attemptId), cancellationToken: cancellationToken).ConfigureAwait(false);
            return new AttemptRecord(attemptId, BudgetDenied: true, reservation.DenialCode, ModelInvoked: false,
                AttemptOutcome.Blocked, "Complete", "budget-denied", "host-authority-denied", "local-budget-ledger",
                [], [], null, null, null, reservation.DenialCode, Settled: true);
        }
        _services.Faults.AfterReservationAppend?.Invoke(attemptId);

        _services.Faults.BeforeIntentAppend?.Invoke(attemptId);
        await _services.Ledger.AppendAsync(EvidenceLedger.AttemptStream(attemptId), EvidenceLedger.DispatchIntent,
            new DispatchIntentEvidence("1.0", attemptId, request.RunId, request.CaseId, request.TrialId, request.TrialSlot,
                request.ModelId, request.ScenarioId, string.Join(",", request.ObservedContractIds),
                request.FixtureId, request.FixtureHash, EvidenceLedger.BoundarySyntheticTransport),
            idempotencyKey: IntentKey(attemptId), cancellationToken: cancellationToken).ConfigureAwait(false);
        _services.Faults.AfterIntentAppend?.Invoke(attemptId);

        _services.Faults.BeforeInvocation?.Invoke(attemptId);
        QualificationResultV3 result;
        try
        {
            using var deadline = new CancellationTokenSource(
                TimeSpan.Parse(request.Deadlines.AttemptTimeout, System.Globalization.CultureInfo.InvariantCulture),
                _services.Time);
            result = await _services.Invoker.InvokeAsync(request, cancellationToken, deadline.Token).ConfigureAwait(false);
            // A worker interruption (ExecuteAsync token cancelled without a
            // durable Cancelled status) must not commit this attempt: the run
            // stays resumable and the intent remains the only evidence until
            // reconciliation.
            if (cancellationToken.IsCancellationRequested)
            {
                var interrupted = await Probe(input.Binding.WorkflowRunId, CancellationToken.None).ConfigureAwait(false);
                if (interrupted?.Status != WorkflowStatus.Cancelled)
                    throw new OperationCanceledException(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The attempt deadline fired: a bounded, observed, time-limited
            // attempt. Record it, then let the step fail so recovery is
            // explicit rather than silently green.
            await RecordCancelledAsync(request, attemptId, "timeout", AttemptOutcome.Blocked).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Either the operator cancelled the run (durable Cancelled status)
            // or the worker was interrupted (lease released, run remains
            // Running and will resume). Only a durable cancellation records a
            // cancelled attempt; a pure interruption leaves the intent for
            // reconciliation, because no outcome exists yet.
            var run = await Probe(input.Binding.WorkflowRunId, CancellationToken.None).ConfigureAwait(false);
            if (run?.Status == WorkflowStatus.Cancelled)
            {
                await RecordCancelledAsync(request, attemptId, "caller-cancelled", AttemptOutcome.Cancelled).ConfigureAwait(false);
            }
            throw;
        }
        _services.Faults.AfterInvocation?.Invoke(attemptId);

        var assertions = EvidenceMapping.Assertions(result, request);
        var outcome = EvidenceMapping.Outcome(result);
        var finished = new AttemptFinishedEvidence("1.0", attemptId, request.RunId, request.CaseId, request.TrialId,
            request.TrialSlot, request.ModelId, request.ScenarioId, outcome.ToString(),
            EvidenceMapping.Completeness(assertions), EvidenceMapping.FailureCategory(result),
            EvidenceMapping.Attribution(outcome, result), EvidenceMapping.AttributionBasis(result), ModelInvoked: true,
            request.AssertionIds, assertions, EvidenceMapping.Diagnostics(result), EvidenceMapping.Router(result),
            EvidenceMapping.Usage(result), null);
        _services.Faults.BeforeFinishedAppend?.Invoke(attemptId);
        await _services.Ledger.AppendAsync(EvidenceLedger.AttemptStream(attemptId), EvidenceLedger.AttemptFinished,
            finished, idempotencyKey: FinishedKey(attemptId), cancellationToken: cancellationToken).ConfigureAwait(false);
        _services.Faults.AfterFinishedAppend?.Invoke(attemptId);
        return new AttemptRecord(attemptId, BudgetDenied: false, null, ModelInvoked: true, outcome,
            EvidenceMapping.Completeness(assertions), EvidenceMapping.FailureCategory(result),
            EvidenceMapping.Attribution(outcome, result), EvidenceMapping.AttributionBasis(result),
            EvidenceMapping.FailedAssertions(assertions), assertions, finished.Diagnostics, finished.Router,
            finished.Usage, null, Settled: false);
    }

    private async Task RecordCancelledAsync(CaseExecutionRequest request, string attemptId, string category,
        AttemptOutcome outcome)
    {
        var finished = new AttemptFinishedEvidence("1.0", attemptId, request.RunId, request.CaseId, request.TrialId,
            request.TrialSlot, request.ModelId, request.ScenarioId, outcome.ToString(), "Incomplete", category,
            "not-attributed-infrastructure-or-incomplete", "incomplete", ModelInvoked: true,
            request.AssertionIds, request.AssertionIds.Select(id => new AssertionEvidence(id, "NotObserved", "execution-incomplete")).ToArray(),
            null, null, null, category);
        var settle = new UsageSettledEvidence("1.0", attemptId, request.RunId, request.CaseId, request.TrialId,
            request.TrialSlot, new UsageEvidence(null, null, null, null, "Unknown", "unavailable"), "spent-unknown");
        await _services.Ledger.AppendAsync(EvidenceLedger.AttemptStream(attemptId), EvidenceLedger.AttemptFinished,
            finished, idempotencyKey: FinishedKey(attemptId), cancellationToken: CancellationToken.None).ConfigureAwait(false);
        await _services.Ledger.AppendAsync(EvidenceLedger.AttemptStream(attemptId), EvidenceLedger.UsageSettled,
            settle, idempotencyKey: SettleKey(attemptId), cancellationToken: CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<SettledAttemptInput> SettleAsync(SettledAttemptInput input, CancellationToken cancellationToken)
    {
        var request = input.Request;
        var attempt = input.Attempt;
        if (attempt.Settled || attempt.BudgetDenied) return input;
        _services.Faults.BeforeSettlementAppend?.Invoke(attempt.AttemptId);
        var usage = attempt.Usage is { Completeness: "Complete" } observed
            ? observed
            : new UsageEvidence(null, null, null, null, "Unknown", "unavailable");
        var settlement = usage.Completeness == "Complete" ? "observed" : "spent-unknown";
        await _services.Ledger.AppendAsync(EvidenceLedger.AttemptStream(attempt.AttemptId), EvidenceLedger.UsageSettled,
            new UsageSettledEvidence("1.0", attempt.AttemptId, request.RunId, request.CaseId, request.TrialId,
                request.TrialSlot, usage, settlement),
            idempotencyKey: SettleKey(attempt.AttemptId), cancellationToken: cancellationToken).ConfigureAwait(false);
        _services.Faults.AfterSettlementAppend?.Invoke(attempt.AttemptId);
        return input with { Attempt = attempt with { Settled = true } };
    }

    private async Task<TrialOutcomeRecord> FinalizeAsync(SettledAttemptInput input, CancellationToken cancellationToken)
    {
        var request = input.Request;
        var attempt = input.Attempt;
        _services.Faults.BeforeTrialFinishedAppend?.Invoke(attempt.AttemptId);
        var outcome = attempt.Outcome;
        var complete = outcome == AttemptOutcome.Passed || attempt.BudgetDenied
            || attempt.Completeness == "Complete";
        var trial = new TrialFinishedEvidence("1.0", request.RunId, request.CaseId, request.TrialId, request.TrialSlot,
            request.ModelId, request.ScenarioId, outcome.ToString(), attempt.FailedAssertionIds, attempt.FailureCategory,
            attempt.Attribution, complete, [attempt.AttemptId]);
        await _services.Ledger.AppendAsync(EvidenceLedger.TrialStream(request.CaseId, request.TrialSlot),
            EvidenceLedger.TrialFinished, trial, idempotencyKey: TrialFinishedKey(request),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _services.Faults.AfterTrialFinishedAppend?.Invoke(attempt.AttemptId);
        var verification = await _services.Ledger.VerifyAsync(null, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
            throw new EvidenceIoException(EvidenceLedger.TrialFinished,
                new InvalidOperationException(verification.Detail ?? "Ledger verification failed."));
        return new TrialOutcomeRecord(attempt.AttemptId, outcome, attempt.FailedAssertionIds, attempt.FailureCategory,
            attempt.Attribution, complete);
    }

    public static string ReserveKey(string attemptId) => $"reserve-{attemptId}";
    public static string DeniedKey(string attemptId) => $"denied-{attemptId}";
    public static string IntentKey(string attemptId) => $"intent-{attemptId}";
    public static string FinishedKey(string attemptId) => $"finished-{attemptId}";
    public static string SettleKey(string attemptId) => $"settle-{attemptId}";
    public static string ReleaseKey(string attemptId) => $"release-{attemptId}";
    public static string InterruptedKey(string attemptId) => $"interrupted-{attemptId}";
    public static string TrialFinishedKey(CaseExecutionRequest request) => $"trial-finished-{request.CaseId}-{request.TrialSlot}";
    public static Guid StableRunId(string planHash, string caseId, string trialId)
    {
        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{Name}@{Version}|{planHash}|{caseId}|{trialId}"));
        return new Guid(digest.AsSpan(0, 16));
    }
}

internal sealed record BindingResult(Guid WorkflowRunId, string Digest);

// Pure, replayable registry-consistency check. A changed contract registry or
// fixture digests fail here before any reservation, secret or invocation.
internal static class BindingCheck
{
    public static BindingResult Run(BindingInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = input.Request;
        var registry = ContractRegistry.Find(request.ScenarioId);
        ExecutionAssert.True(registry.AssertionIds.SequenceEqual(request.AssertionIds, StringComparer.Ordinal),
            "assertion-set-changed");
        ExecutionAssert.True(registry.FixtureHash == request.FixtureHash, "fixture-changed");
        ExecutionAssert.True(registry.MaximumDispatches == request.DispatchCeiling, "dispatch-ceiling-changed");
        foreach (var observed in request.ObservedContractIds)
            ExecutionAssert.True(ContractRegistry.Current.Any(c => c.Id == observed), "observed-contract-unknown");
        ExecutionAssert.True(request.ObservedContractIds.Contains(request.ScenarioId, StringComparer.Ordinal),
            "scenario-not-observed");
        var digest = CanonicalJson.Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("|",
            input.WorkflowRunId, request.PlanHash, request.CaseId, request.TrialId, request.SubjectId,
            request.ScenarioId, request.FixtureHash, string.Join(",", request.AssertionIds),
            string.Join(",", request.ObservedContractIds))));
        return new BindingResult(input.WorkflowRunId, digest);
    }
}

internal static class ExecutionAssert
{
    public static void True(bool condition, string code)
    {
        if (!condition) throw new ExecutionException(code);
    }
}
