namespace Penghou.Baize.Generation;

/// <summary>
/// The outcome of one logical batch chunk. Chunks are indexed so callers can
/// map results back to the request grid.
/// </summary>
/// <param name="Index">The zero-based chunk index in the batch.</param>
/// <param name="Result">The chunk result, when the chunk succeeded.</param>
/// <param name="Error">The normalized failure, when the chunk failed. Null on success.</param>
public sealed record GenerationBatchChunk(
    int Index,
    GenerationResult? Result,
    BaizeException? Error);

/// <summary>
/// The aggregate result of a logical generation batch. The batch never throws on
/// per-chunk provider failures: each chunk's outcome is recorded in
/// <see cref="Chunks"/> and the successful assets are aggregated in
/// <see cref="Assets"/>, so callers get explicit partial results and decide how
/// strict to be. Only batch-level failures (no suitable endpoint, request
/// validation, cancellation) surface as exceptions.
/// </summary>
/// <param name="Chunks">Every chunk outcome, in submission order.</param>
/// <param name="RequestedCount">The <see cref="GenerationBatchRequest.TotalCount"/> requested.</param>
public sealed record GenerationBatchResult(
    IReadOnlyList<GenerationBatchChunk> Chunks,
    int RequestedCount)
{
    /// <summary>The number of chunks that succeeded.</summary>
    public int SucceededCount { get; } = Chunks.Count(chunk => chunk.Error is null);

    /// <summary>The number of chunks that failed.</summary>
    public int FailedCount { get; } = Chunks.Count(chunk => chunk.Error is not null);

    /// <summary>Whether every chunk succeeded.</summary>
    public bool AllSucceeded => FailedCount == 0;

    /// <summary>
    /// Every asset produced by the successful chunks, in chunk order. Chunk
    /// failure gaps are not represented, so asset position is not a reliable
    /// request-grid index; use <see cref="Chunks"/> for grid mapping.
    /// </summary>
    public IReadOnlyList<GeneratedAsset> Assets { get; } =
        Chunks
            .Where(chunk => chunk.Result is not null)
            .SelectMany(chunk => chunk.Result!.Assets)
            .ToArray();

    /// <summary>
    /// The normalized failures of the failed chunks, in chunk order, or an empty
    /// collection when none failed.
    /// </summary>
    public IReadOnlyList<BaizeException> Errors { get; } =
        Chunks
            .Where(chunk => chunk.Error is not null)
            .Select(chunk => chunk.Error!)
            .ToArray();
}