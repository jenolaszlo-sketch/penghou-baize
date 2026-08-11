namespace Penghou.Baize.Batch;

/// <summary>
/// Reports a logical submission failure while preserving handles for physical
/// batches that were already accepted, so callers can persist and reconcile
/// them instead of accidentally submitting duplicates.
/// </summary>
public sealed class BaizeBatchSubmissionException : Exception
{
    /// <summary>Initializes a partial-submission failure.</summary>
    public BaizeBatchSubmissionException(
        string message,
        BaizeBatchHandle partialHandle,
        Exception innerException)
        : base(message, innerException)
    {
        PartialHandle = partialHandle;
        Failures = [new BaizeBatchSubmissionFailure(-1, string.Empty, innerException)];
    }

    /// <summary>Initializes an aggregate physical-submission failure.</summary>
    public BaizeBatchSubmissionException(
        string message,
        BaizeBatchHandle partialHandle,
        IReadOnlyList<BaizeBatchSubmissionFailure> failures)
        : base(
            message,
            failures.Count > 0
                ? failures[0].Error
                : new InvalidOperationException("No submission failure was supplied."))
    {
        ArgumentNullException.ThrowIfNull(failures);
        PartialHandle = partialHandle;
        Failures = failures;
    }

    /// <summary>The provider batches accepted before submission failed.</summary>
    public BaizeBatchHandle PartialHandle { get; }

    /// <summary>Every physical submission that failed, in plan order.</summary>
    public IReadOnlyList<BaizeBatchSubmissionFailure> Failures { get; }
}
