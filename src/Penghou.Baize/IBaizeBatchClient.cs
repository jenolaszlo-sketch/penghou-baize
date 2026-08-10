namespace Penghou.Baize;

/// <summary>
/// A provider-specific asynchronous batch client. Implementations own their
/// native batch protocol (submission, polling, cancellation, and result
/// retrieval) and remain stateless: reconnect to an existing operation purely
/// through the serializable <see cref="ProviderBatchHandle"/>.
/// </summary>
public interface IBaizeBatchClient
{
    /// <summary>The provider adapter that owns this client.</summary>
    string ProviderId { get; }

    /// <summary>The batch operations the configured endpoint supports.</summary>
    BatchCapabilities Capabilities { get; }

    /// <summary>
    /// Submits a group of requests as a single provider batch.
    /// </summary>
    /// <param name="items">The requests to submit, each with its stable correlation identifier.</param>
    /// <param name="options">Submission options, when any.</param>
    /// <param name="cancellationToken">Propagates notification that submission should be cancelled.</param>
    /// <returns>The serializable provider batch handle.</returns>
    Task<ProviderBatchHandle> SubmitAsync(
        IReadOnlyList<BaizeBatchItem> items,
        BatchSubmissionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the normalized status of a previously submitted batch.</summary>
    /// <param name="handle">The handle of the provider batch to poll.</param>
    /// <param name="cancellationToken">Propagates notification that polling should be cancelled.</param>
    /// <returns>The normalized provider batch status.</returns>
    Task<ProviderBatchStatus> GetStatusAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the normalized results of a completed provider batch.</summary>
    /// <param name="handle">The handle of the provider batch to retrieve results for.</param>
    /// <param name="cancellationToken">Propagates notification that retrieval should be cancelled.</param>
    /// <returns>The normalized per-request results.</returns>
    Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation of a running provider batch, when supported.</summary>
    /// <param name="handle">The handle of the provider batch to cancel.</param>
    /// <param name="cancellationToken">Propagates notification that cancellation should be cancelled.</param>
    /// <exception cref="NotSupportedException">
    /// Thrown when <see cref="Capabilities"/> does not include
    /// <see cref="BatchCapabilities.Cancellation"/>.
    /// </exception>
    Task CancelAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default);
}
