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
    }

    /// <summary>The provider batches accepted before submission failed.</summary>
    public BaizeBatchHandle PartialHandle { get; }
}
