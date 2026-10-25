using System.Text.Json;
using Penghou.Siming;

namespace Penghou.Baize.Qualification.Execution;

// Budget accounting authority: the frozen plan policy is the envelope, the
// Siming budget stream is the committed consumption record. Remaining
// allowance is recomputed from the ledger on every reservation; the CAS append
// makes over-allocation impossible even when writers race. Nothing replenishes
// on restart because the envelope and the ledger are both persisted.
internal sealed class BudgetState
{
    public required long RemainingModelCalls { get; init; }
    public required long RemainingHttpRequests { get; init; }
    public required long? RemainingInputTokens { get; init; }
    public required long? RemainingOutputTokens { get; init; }
    public required long? RemainingReasoningTokens { get; init; }
    public required long? RemainingTotalTokens { get; init; }
    public long ConsumedModelCalls { get; init; }
    public long ConsumedHttpRequests { get; init; }
}

internal sealed record ReservationOutcome(string? DenialCode, ReservationEvidence? Reserved);

internal static class BudgetLedger
{
    public static async Task<BudgetState> ComputeAsync(EvidenceLedger ledger, BudgetEnvelopeEvidence envelope,
        CancellationToken cancellationToken)
    {
        var consumedModelCalls = 0L;
        var consumedHttpRequests = 0L;
        var consumedInput = 0L;
        var consumedOutput = 0L;
        var consumedReasoning = 0L;
        var consumedTotal = 0L;
        await foreach (var entry in ledger.ReadAllAsync(EvidenceLedger.BudgetStream, cancellationToken))
        {
            using var document = JsonDocument.Parse(entry.Payload);
            var payload = document.RootElement;
            switch (entry.EventType)
            {
                case EvidenceLedger.AttemptReserved:
                    consumedModelCalls += payload.GetProperty("units").GetProperty("modelCalls").GetInt64();
                    consumedHttpRequests += payload.GetProperty("units").GetProperty("httpRequests").GetInt64();
                    consumedInput += Nullable(payload.GetProperty("units").GetProperty("inputTokens"));
                    consumedOutput += Nullable(payload.GetProperty("units").GetProperty("outputTokens"));
                    consumedReasoning += Nullable(payload.GetProperty("units").GetProperty("reasoningTokens"));
                    consumedTotal += Nullable(payload.GetProperty("units").GetProperty("totalTokens"));
                    break;
                case EvidenceLedger.AttemptReleased:
                    consumedModelCalls -= 1;
                    consumedHttpRequests -= 1;
                    consumedInput -= Nullable(payload.GetProperty("units").GetProperty("inputTokens"));
                    consumedOutput -= Nullable(payload.GetProperty("units").GetProperty("outputTokens"));
                    consumedReasoning -= Nullable(payload.GetProperty("units").GetProperty("reasoningTokens"));
                    consumedTotal -= Nullable(payload.GetProperty("units").GetProperty("totalTokens"));
                    break;
            }
        }
        return new BudgetState
        {
            RemainingModelCalls = envelope.ModelCalls - consumedModelCalls,
            RemainingHttpRequests = envelope.HttpRequests - consumedHttpRequests,
            RemainingInputTokens = Subtract(envelope.InputTokens, consumedInput),
            RemainingOutputTokens = Subtract(envelope.OutputTokens, consumedOutput),
            RemainingReasoningTokens = Subtract(envelope.ReasoningTokens, consumedReasoning),
            RemainingTotalTokens = Subtract(envelope.TotalTokens, consumedTotal),
            ConsumedModelCalls = consumedModelCalls,
            ConsumedHttpRequests = consumedHttpRequests
        };
    }

    private static long Nullable(JsonElement element) => element.ValueKind == JsonValueKind.Null ? 0 : element.GetInt64();
    private static long? Subtract(long? ceiling, long consumed) => ceiling is null ? null : ceiling - consumed;

    public static string? DenialCode(BudgetState state, BudgetUnitsEvidence units) =>
        state.RemainingModelCalls < units.ModelCalls ? "budget-model-calls-exhausted"
        : state.RemainingHttpRequests < units.HttpRequests ? "budget-http-requests-exhausted"
        : Exceeded(state.RemainingInputTokens, units.InputTokens) is { } input ? input
        : Exceeded(state.RemainingOutputTokens, units.OutputTokens) is { } output ? output
        : Exceeded(state.RemainingReasoningTokens, units.ReasoningTokens) is { } reasoning ? reasoning
        : Exceeded(state.RemainingTotalTokens, units.TotalTokens) is { } total ? total
        : null;

    private static string? Exceeded(long? remaining, long? units) =>
        units is not null && remaining is not null && remaining < units ? "budget-token-envelope-insufficient" : null;

    public static BudgetUnitsEvidence UnitsFor(CaseExecutionRequest request) => new(
        1, 1,
        request.Budget.InputTokens is null ? null : ReservedInput(request),
        request.Budget.OutputTokens is null ? null : ReservedOutput(request),
        request.Budget.ReasoningTokens is null ? null : ReservedReasoning(request),
        request.Budget.TotalTokens is null ? null : ReservedTotal(request));

    // A reservation is the plan's requested per-dispatch reservation when the
    // policy carries a ceiling for that unit; otherwise the unit is not
    // admitted by budget (recorded as observation only).
    private static long? ReservedInput(CaseExecutionRequest request) =>
        PerDispatch(request.PlanCaseTokenReservation?.InputTokens, request.DispatchCeiling);
    private static long? ReservedOutput(CaseExecutionRequest request) =>
        PerDispatch(request.PlanCaseTokenReservation?.OutputTokens, request.DispatchCeiling);
    private static long? ReservedReasoning(CaseExecutionRequest request) =>
        PerDispatch(request.PlanCaseTokenReservation?.ReasoningTokens, request.DispatchCeiling);
    private static long? ReservedTotal(CaseExecutionRequest request) =>
        PerDispatch(request.PlanCaseTokenReservation?.TotalTokens, request.DispatchCeiling);
    private static long? PerDispatch(long? reservation, int dispatches) =>
        reservation is null || dispatches <= 0 ? null : Math.Max(0, (reservation.Value + dispatches - 1) / dispatches);

    // Atomically consumes the allowance under optimistic concurrency. The CAS
    // expected-head append guarantees a racing writer cannot double-allocate
    // the final slot; a denial records no reservation and consumes nothing.
    public static async Task<ReservationOutcome> ReserveAsync(EvidenceLedger ledger, CaseExecutionRequest request,
        string attemptId, BudgetUnitsEvidence units, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var head = await ledger.GetHeadAsync(cancellationToken).ConfigureAwait(false);
            var state = await ComputeAsync(ledger, request.Budget, cancellationToken).ConfigureAwait(false);
            var denial = DenialCode(state, units);
            if (denial is not null) return new ReservationOutcome(denial, null);
            var evidence = new ReservationEvidence("1.0", attemptId, request.RunId, request.CaseId, request.TrialId,
                request.TrialSlot, request.ModelId, request.ScenarioId, units);
            try
            {
                await ledger.AppendAsync(EvidenceLedger.BudgetStream, EvidenceLedger.AttemptReserved, evidence,
                    idempotencyKey: QualificationWorkflow.ReserveKey(attemptId), expectedHead: head,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return new ReservationOutcome(null, evidence);
            }
            catch (LedgerHeadConflictException) when (attempt < 8)
            {
            }
        }
    }
}
