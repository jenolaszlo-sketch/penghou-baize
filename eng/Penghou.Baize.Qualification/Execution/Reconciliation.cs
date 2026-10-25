using System.Text.Json;

namespace Penghou.Baize.Qualification.Execution;

internal sealed record ReconciliationResult(int Released, int Interrupted, int Settled);

// Idempotent reconciliation between the Zhinu-committed workflow state and the
// Siming evidence ledger. It never invokes a model, never fabricates a
// successful trial and never releases a reservation that could have reached a
// backend: reserved-without-intent is released, intent-without-result is
// interrupted with full conservative liability, finished-without-settlement
// settles from the recorded observation.
internal static class Reconciliation
{
    public static async Task<ReconciliationResult> RunAsync(EvidenceLedger ledger, string runId, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var reservations = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var reservedUnits = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var intents = new HashSet<string>(StringComparer.Ordinal);
        var finished = new HashSet<string>(StringComparer.Ordinal);
        var finishedUsage = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var settled = new HashSet<string>(StringComparer.Ordinal);
        var released = new HashSet<string>(StringComparer.Ordinal);
        var interrupted = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var entry in ledger.ReadAllAsync(null, cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(entry.Payload);
            var payload = document.RootElement.Clone();
            var attemptId = payload.TryGetProperty("attemptId", out var id) ? id.GetString()! : null;
            if (attemptId is null) continue;
            switch (entry.EventType)
            {
                case EvidenceLedger.AttemptReserved:
                    reservations[attemptId] = payload;
                    break;
                case EvidenceLedger.AttemptReleased:
                    released.Add(attemptId);
                    break;
                case EvidenceLedger.DispatchIntent:
                    intents.Add(attemptId);
                    break;
                case EvidenceLedger.AttemptFinished:
                    finished.Add(attemptId);
                    if (payload.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                        finishedUsage[attemptId] = usage;
                    break;
                case EvidenceLedger.UsageSettled:
                    settled.Add(attemptId);
                    break;
                case EvidenceLedger.AttemptInterrupted:
                    interrupted.Add(attemptId);
                    break;
            }
        }
        var releasedCount = 0;
        var interruptedCount = 0;
        var settledCount = 0;
        var attempts = reservations.Keys.Concat(intents).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var attemptId in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reservations.TryGetValue(attemptId, out var reservation))
            {
                var releasedEvidence = new ReservationReleasedEvidence("1.0", attemptId,
                    reservation.GetProperty("runId").GetString()!, reservation.GetProperty("caseId").GetString()!,
                    reservation.GetProperty("trialId").GetString()!, reservation.GetProperty("trialSlot").GetInt32(),
                    "reserved-without-intent");
                if (!intents.Contains(attemptId) && !released.Contains(attemptId))
                {
                    await ledger.AppendAsync(EvidenceLedger.BudgetStream, EvidenceLedger.AttemptReleased,
                        releasedEvidence, idempotencyKey: QualificationWorkflow.ReleaseKey(attemptId),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    releasedCount++;
                    continue;
                }
            }
            if (!intents.Contains(attemptId)) continue;
            var reservationPayload = reservations.TryGetValue(attemptId, out var reservedPayload)
                ? reservedPayload : DefaultReservation(attemptId, runId);
            if (!finished.Contains(attemptId))
            {
                if (!interrupted.Contains(attemptId))
                {
                    await ledger.AppendAsync(EvidenceLedger.AttemptStream(attemptId), EvidenceLedger.AttemptInterrupted,
                        new AttemptInterruptedEvidence("1.0", attemptId,
                            reservationPayload.GetProperty("runId").GetString()!,
                            reservationPayload.GetProperty("caseId").GetString()!,
                            reservationPayload.GetProperty("trialId").GetString()!,
                            reservationPayload.GetProperty("trialSlot").GetInt32(),
                            "intent-without-result"),
                        idempotencyKey: QualificationWorkflow.InterruptedKey(attemptId),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    interruptedCount++;
                }
                if (!settled.Contains(attemptId))
                {
                    await ledger.AppendAsync(EvidenceLedger.AttemptStream(attemptId), EvidenceLedger.UsageSettled,
                        new UsageSettledEvidence("1.0", attemptId,
                            reservationPayload.GetProperty("runId").GetString()!,
                            reservationPayload.GetProperty("caseId").GetString()!,
                            reservationPayload.GetProperty("trialId").GetString()!,
                            reservationPayload.GetProperty("trialSlot").GetInt32(),
                            new UsageEvidence(null, null, null, null, "Unknown", "unavailable"), "spent-unknown"),
                        idempotencyKey: QualificationWorkflow.SettleKey(attemptId),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    settledCount++;
                }
                continue;
            }
            if (!settled.Contains(attemptId) && !interrupted.Contains(attemptId))
            {
                var usage = finishedUsage.TryGetValue(attemptId, out var recorded)
                    && recorded.TryGetProperty("completeness", out var completeness)
                    && completeness.GetString() == "Complete"
                    ? new UsageEvidence(ReadNullableLong(recorded, "promptTokens"), ReadNullableLong(recorded, "completionTokens"),
                        ReadNullableLong(recorded, "thinkingTokens"), ReadNullableLong(recorded, "totalTokens"),
                        "Complete", "synthetic-terminal-transport")
                    : new UsageEvidence(null, null, null, null, "Unknown", "unavailable");
                var settlement = usage.Completeness == "Complete" ? "observed" : "spent-unknown";
                await ledger.AppendAsync(EvidenceLedger.AttemptStream(attemptId), EvidenceLedger.UsageSettled,
                    new UsageSettledEvidence("1.0", attemptId, reservationPayload.GetProperty("runId").GetString()!,
                        reservationPayload.GetProperty("caseId").GetString()!, reservationPayload.GetProperty("trialId").GetString()!,
                        reservationPayload.GetProperty("trialSlot").GetInt32(), usage, settlement),
                    idempotencyKey: QualificationWorkflow.SettleKey(attemptId),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                settledCount++;
            }
        }
        return new ReconciliationResult(releasedCount, interruptedCount, settledCount);
    }

    private static JsonElement DefaultReservation(string attemptId, string runId) =>
        JsonSerializer.SerializeToElement(new { runId, caseId = "unknown", trialId = "unknown", trialSlot = 0 });

    private static long? ReadNullableLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
}
