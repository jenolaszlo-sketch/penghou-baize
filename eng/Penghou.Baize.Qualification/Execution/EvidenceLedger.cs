using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Siming;
using Penghou.Siming.Sqlite;

namespace Penghou.Baize.Qualification.Execution;

// The single authoritative evidence port. Every qualification observation
// passes through the pinned Penghou.Siming.Sqlite ledger; Baize implements no
// second journal, hash chain or writer-lock protocol.
internal sealed class EvidenceLedger : IAsyncDisposable
{
    public const string RunStream = "run";
    public const string BudgetStream = "budget";
    public const string BoundarySyntheticTransport = "synthetic-terminal-transport";

    public const string RunStarted = "QualificationRunStarted";
    public const string RunFinished = "QualificationRunFinished";
    public const string Reconciliation = "QualificationReconciliation";
    public const string AttemptReserved = "QualificationAttemptReserved";
    public const string AttemptReleased = "QualificationAttemptReleased";
    public const string BudgetDenied = "QualificationBudgetDenied";
    public const string DispatchIntent = "QualificationDispatchIntent";
    public const string AttemptFinished = "QualificationAttemptFinished";
    public const string UsageSettled = "QualificationUsageSettled";
    public const string AttemptInterrupted = "QualificationAttemptInterrupted";
    public const string TrialFinished = "QualificationTrialFinished";

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly SqliteAppendOnlyLedger<CanonicalJsonPayloadSerializerV2> _ledger;
    private readonly ExecutionFaults _faults;

    public EvidenceLedger(string databasePath, ExecutionFaults faults)
    {
        _faults = faults;
        var serializer = new CanonicalJsonPayloadSerializerV2(Options);
        _ledger = new SqliteAppendOnlyLedger<CanonicalJsonPayloadSerializerV2>(
            new SimingSqliteOptions
            {
                DatabasePath = databasePath,
                InputLimits = new LedgerInputLimits
                {
                    MaxPayloadBytes = 64 * 1024,
                    MaxEventTypeUtf8Bytes = 256,
                    MaxStreamIdUtf8Bytes = 512,
                    MaxIdempotencyKeyUtf8Bytes = 512
                }
            },
            serializer);
    }

    public static string AttemptStream(string attemptId) => $"attempt:{attemptId}";
    public static string TrialStream(string caseId, int trialSlot) => $"trial:{caseId}:{trialSlot}";

    public IAppendOnlyLedger Ledger => _ledger;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public async ValueTask<LedgerEntry> AppendAsync<T>(string streamId, string eventType, T payload,
        string? idempotencyKey = null, LedgerHead? expectedHead = null,
        CancellationToken cancellationToken = default)
    {
        if (_faults.FailEvidenceAppend?.Invoke(eventType) == true)
            throw new EvidenceIoException(eventType, new InvalidOperationException("Injected evidence append failure."));
        return await _ledger.AppendAsync(new LedgerAppendRequest<T>(streamId, eventType, payload)
        {
            IdempotencyKey = idempotencyKey,
            ExpectedHead = expectedHead
        }, cancellationToken).ConfigureAwait(false);
    }

    // Optimistic-concurrency append: recompute the expectation under contention
    // and retry a bounded number of times. Used for budget reservations so two
    // racing reservations cannot both observe the same remaining allowance.
    public async Task<LedgerEntry> AppendCasAsync<T>(string streamId, string eventType, T payload,
        string? idempotencyKey, Func<LedgerHead> expectedHeadFactory, int maximumAttempts,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await AppendAsync(streamId, eventType, payload, idempotencyKey,
                    expectedHeadFactory(), cancellationToken).ConfigureAwait(false);
            }
            catch (LedgerHeadConflictException) when (attempt < maximumAttempts - 1)
            {
            }
        }
    }

    public ValueTask<LedgerHead> GetHeadAsync(CancellationToken cancellationToken = default) =>
        _ledger.GetHeadAsync(cancellationToken);

    public ValueTask<LedgerVerificationResult> VerifyAsync(LedgerCheckpoint? checkpoint = null,
        CancellationToken cancellationToken = default) =>
        _ledger.VerifyAsync(checkpoint, cancellationToken);

    public IAsyncEnumerable<LedgerEntry> ReadAllAsync(string? streamId = null,
        CancellationToken cancellationToken = default) =>
        _ledger.ReadAsync(streamId, cancellationToken);

    public ValueTask DisposeAsync() => _ledger.DisposeAsync();
}
