namespace Penghou.Baize.Generation;

/// <summary>
/// A snapshot of a generation operation. One lifecycle abstraction covers both
/// immediate providers (submit → <see cref="GenerationOperationState.Succeeded"/>
/// with a result) and queued providers (submit → queued + handle, then polling
/// <see cref="IGenerationClient.GetAsync"/> to Succeeded/Failed).
/// </summary>
/// <param name="Handle">The pinned operation identity.</param>
/// <param name="State">The mapped lifecycle state.</param>
/// <param name="Result">The generated result, when the operation succeeded.</param>
/// <param name="Error">The normalized failure, when the provider job failed.</param>
/// <param name="Progress">Progress in the range 0.0–1.0, or null when unavailable.</param>
/// <param name="ProviderMetadata">Provider-specific raw status values for diagnostics.</param>
public sealed record GenerationOperation(
    GenerationOperationHandle Handle,
    GenerationOperationState State,
    GenerationResult? Result = null,
    GenerationError? Error = null,
    double? Progress = null,
    IReadOnlyDictionary<string, object?>? ProviderMetadata = null)
{
    /// <summary>
    /// Typed progress details. <see cref="Progress"/> remains the compatibility
    /// projection of <see cref="GenerationProgress.Fraction"/>.
    /// </summary>
    public GenerationProgress? ProgressDetails { get; init; }
}
