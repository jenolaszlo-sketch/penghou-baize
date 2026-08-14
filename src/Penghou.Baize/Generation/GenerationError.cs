namespace Penghou.Baize.Generation;

/// <summary>
/// A normalized failure recorded on a <see cref="GenerationOperation"/> after a
/// provider-side job failed. This deliberately mirrors <see cref="BaizeError"/>
/// while carrying the generation-specific <see cref="GenerationErrorKind"/>
/// taxonomy that the batch vocabulary cannot express.
/// </summary>
/// <param name="Kind">The normalized failure classification.</param>
/// <param name="Message">The human-readable failure description.</param>
/// <param name="StatusCode">The provider HTTP status code, when one was reported.</param>
/// <param name="ProviderStatus">The raw provider-side error text, for diagnostics.</param>
public sealed record GenerationError(
    GenerationErrorKind Kind,
    string Message,
    int? StatusCode = null,
    string? ProviderStatus = null);