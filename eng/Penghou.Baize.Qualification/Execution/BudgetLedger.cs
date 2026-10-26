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
    public required string? RemainingCost { get; init; }
    public long ConsumedModelCalls { get; init; }
    public long ConsumedHttpRequests { get; init; }
    public required long DispatchedHttpRequests { get; init; }
    public required long RemainingHttpDispatches { get; init; }
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
        var consumedCost = 0m;
        var dispatchedHttpRequests = 0L;
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
                    consumedCost += Money(payload.GetProperty("units"));
                    break;
                case EvidenceLedger.AttemptReleased:
                    consumedModelCalls -= 1;
                    consumedHttpRequests -= 1;
                    consumedInput -= Nullable(payload.GetProperty("units").GetProperty("inputTokens"));
                    consumedOutput -= Nullable(payload.GetProperty("units").GetProperty("outputTokens"));
                    consumedReasoning -= Nullable(payload.GetProperty("units").GetProperty("reasoningTokens"));
                    consumedTotal -= Nullable(payload.GetProperty("units").GetProperty("totalTokens"));
                    consumedCost -= Money(payload.GetProperty("units"));
                    break;
                case EvidenceLedger.HttpDispatchAdmitted:
                    dispatchedHttpRequests++;
                    break;
            }
        }
        var remainingCost = envelope.MaximumCost is null ? null
            : (decimal.Parse(envelope.MaximumCost, System.Globalization.CultureInfo.InvariantCulture) - consumedCost)
                .ToString("0.#############################", System.Globalization.CultureInfo.InvariantCulture);
        return new BudgetState
        {
            RemainingModelCalls = envelope.ModelCalls - consumedModelCalls,
            RemainingHttpRequests = envelope.HttpRequests - consumedHttpRequests,
            RemainingInputTokens = Subtract(envelope.InputTokens, consumedInput),
            RemainingOutputTokens = Subtract(envelope.OutputTokens, consumedOutput),
            RemainingReasoningTokens = Subtract(envelope.ReasoningTokens, consumedReasoning),
            RemainingTotalTokens = Subtract(envelope.TotalTokens, consumedTotal),
            RemainingCost = remainingCost,
            ConsumedModelCalls = consumedModelCalls,
            ConsumedHttpRequests = consumedHttpRequests,
            DispatchedHttpRequests = dispatchedHttpRequests,
            RemainingHttpDispatches = envelope.HttpRequests - dispatchedHttpRequests
        };
    }

    private static decimal Money(JsonElement units)
    {
        if (units.TryGetProperty("maximumCost", out var cost) && cost.ValueKind == JsonValueKind.String)
            return decimal.Parse(cost.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        return 0m;
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
        : CostExceeded(state.RemainingCost, units.MaximumCost, units.Currency) is { } cost ? cost
        : null;

    private static string? CostExceeded(string? remainingCost, string? unitsCost, string? currency)
    {
        if (unitsCost is null || remainingCost is null) return null;
        var remaining = decimal.Parse(remainingCost, System.Globalization.CultureInfo.InvariantCulture);
        var units = decimal.Parse(unitsCost, System.Globalization.CultureInfo.InvariantCulture);
        return units > remaining ? "budget-cost-envelope-insufficient" : null;
    }

    private static string? Exceeded(long? remaining, long? units) =>
        units is not null && remaining is not null && remaining < units ? "budget-token-envelope-insufficient" : null;

    // The worst-case reservation covers every physical attempt the reviewed
    // profile permits for one admitted inference request. The outer application
    // dispatch count alone is not the budget basis.
    public static BudgetUnitsEvidence UnitsFor(CaseExecutionRequest request)
    {
        var physical = Math.Max(1, request.MaximumPhysicalAttempts);
        return new(1, physical,
            request.Budget.InputTokens is null ? null : ReservedInput(request, physical),
            request.Budget.OutputTokens is null ? null : ReservedOutput(request, physical),
            request.Budget.ReasoningTokens is null ? null : ReservedReasoning(request, physical),
            request.Budget.TotalTokens is null ? null : ReservedTotal(request, physical),
            request.Budget.MaximumCost is null ? null : ReservedCost(request, physical),
            request.Budget.Currency);
    }

    private static string? ReservedCost(CaseExecutionRequest request, int physical)
    {
        var reservation = request.PlanCaseTokenReservation?.MaximumCost;
        if (reservation is null) return null;
        var total = decimal.Parse(reservation, System.Globalization.CultureInfo.InvariantCulture) * physical;
        return total.ToString("0.#############################", System.Globalization.CultureInfo.InvariantCulture);
    }

    // The plan's requested token envelope covers one physical inference
    // attempt; the accepted bound scales the conservative reservation.
    private static long? ReservedInput(CaseExecutionRequest request, int physical) =>
        Scale(request.PlanCaseTokenReservation?.InputTokens, physical);
    private static long? ReservedOutput(CaseExecutionRequest request, int physical) =>
        Scale(request.PlanCaseTokenReservation?.OutputTokens, physical);
    private static long? ReservedReasoning(CaseExecutionRequest request, int physical) =>
        Scale(request.PlanCaseTokenReservation?.ReasoningTokens, physical);
    private static long? ReservedTotal(CaseExecutionRequest request, int physical) =>
        Scale(request.PlanCaseTokenReservation?.TotalTokens, physical);
    private static long? Scale(long? reservation, int physical) =>
        reservation is null ? null : checked(reservation.Value * physical);

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
            catch (Microsoft.Data.Sqlite.SqliteException) when (attempt < 8)
            {
                // A concurrent writer held the SQLite lock past the busy
                // timeout; the head will be re-read before the retry.
            }
        }
    }
}
