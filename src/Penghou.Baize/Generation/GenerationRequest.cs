namespace Penghou.Baize.Generation;

/// <summary>
/// The provider-neutral base for artifact-generation requests. Modality-specific
/// requests (<see cref="ImageGenerationRequest"/>, <see cref="VideoGenerationRequest"/>,
/// <see cref="AudioGenerationRequest"/>) carry only the fields that modality needs.
/// The common request never selects the provider; routing is done separately.
/// </summary>
public abstract record GenerationRequest
{
    /// <summary>
    /// An application-supplied idempotency key. Providers that support idempotent
    /// submission forward it so a retry after ambiguity cannot create duplicates.
    /// Null means the caller is not asserting idempotency.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
