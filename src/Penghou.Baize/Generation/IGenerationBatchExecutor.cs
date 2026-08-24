namespace Penghou.Baize.Generation;

/// <summary>
/// Executes a logical generation batch: splits a base request across a total
/// count into chunks bounded by the endpoint's native candidate limit, submits
/// every chunk with bounded concurrency, and waits for queued handles to reach
/// a terminal state in concurrent polling waves. Synchronous providers that
/// return a terminal operation from submission skip the poll phase. Where a
/// single operation cannot produce all requested outputs (native count below
/// the total, or a provider without multiple candidates), the batch still
/// completes the remaining slots.
/// </summary>
public interface IGenerationBatchExecutor
{
    /// <summary>
    /// Routes, submits, and waits for every chunk of a logical generation batch.
    /// Per-chunk provider failures are recorded on the returned
    /// <see cref="GenerationBatchResult"/> rather than thrown; batch-level
    /// failures (no suitable endpoint, validation, cancellation) surface as
    /// exceptions.
    /// </summary>
    /// <param name="request">The logical batch to execute.</param>
    /// <param name="progress">Optional overall progress reporting (0.0–1.0 scale across the whole batch).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The aggregate batch result.</returns>
    Task<GenerationBatchResult> ExecuteAsync(
        GenerationBatchRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a single batch-chunk operation accepted earlier — for example
    /// one whose wait hit the executor timeout — by polling the endpoint
    /// pinned in the handle until it reaches a terminal state. The pinned
    /// endpoint is resolved from the registry; routing is never re-run.
    /// </summary>
    /// <param name="handle">The chunk operation's handle.</param>
    /// <param name="progress">Optional progress reporting (0.0–1.0 scale).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The final generation result of the chunk.</returns>
    Task<GenerationResult> WaitAsync(
        GenerationOperationHandle handle,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}