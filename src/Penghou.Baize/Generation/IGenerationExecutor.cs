namespace Penghou.Baize.Generation;

/// <summary>
/// A convenience executor that turns a single generation request into a final
/// <see cref="GenerationResult"/>. It selects an endpoint via the
/// <see cref="IGenerationRoutingPolicy"/>, submits exactly once, pins the
/// returned handle, polls with backoff, reports progress, enforces a timeout,
/// and returns the terminal result. It is intentionally in-process and
/// non-durable; durable orchestration belongs in an application-level executor.
/// </summary>
public interface IGenerationExecutor
{
    /// <summary>
    /// Routes, submits, and waits for a generation request. The request is
    /// submitted at most once: ambiguous submission outcomes surface as
    /// <see cref="BaizeException"/> and are never replayed automatically.
    /// </summary>
    /// <param name="request">The modality-specific generation request.</param>
    /// <param name="progress">Optional progress reporting (0.0–1.0 scale).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The final generation result.</returns>
    Task<GenerationResult> ExecuteAsync(
        GenerationRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}