namespace Penghou.Baize.Batch;

/// <summary>
/// Performs one-shot operations across the physical provider batches in a
/// logical batch. It does not poll, persist, schedule, or retry workflows.
/// </summary>
public interface IBaizeBatchCoordinator
{
    /// <summary>Plans and submits every physical provider batch.</summary>
    Task<BaizeBatchHandle> SubmitAsync(
        BaizeBatchSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an aggregate snapshot of all physical batch statuses.</summary>
    Task<BaizeBatchStatus> GetStatusAsync(
        BaizeBatchHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves and correlates results from all physical batches.</summary>
    Task<BaizeBatchResultSet> GetResultsAsync(
        BaizeBatchHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation of every physical batch.</summary>
    Task CancelAsync(
        BaizeBatchHandle handle,
        CancellationToken cancellationToken = default);
}
