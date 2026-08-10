namespace Penghou.Baize;

/// <summary>
/// A normalized failure for a single request in an asynchronous batch, retaining
/// the provider's classification and diagnostics.
/// </summary>
/// <param name="Message">The human-readable failure description.</param>
/// <param name="FailureKind">
/// The normalized failure classification, derived from the provider error shape
/// the same way <see cref="LlmClientException"/> classifies HTTP failures.
/// </param>
/// <param name="StatusCode">The provider HTTP status code, when one was reported.</param>
/// <param name="ProviderStatus">The raw provider-side error text, for diagnostics.</param>
public sealed record BaizeError(
    string Message,
    LlmClientFailureKind FailureKind,
    int? StatusCode = null,
    string? ProviderStatus = null);
