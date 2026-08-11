namespace Penghou.Baize;

/// <summary>
/// The normalized status of a single provider batch, retaining the provider's
/// raw status text and any reported counts for diagnostics.
/// </summary>
/// <param name="State">The normalized batch state.</param>
/// <param name="ProviderStatus">The raw provider status text, when reported.</param>
/// <param name="Total">The number of requests in the batch, when reported.</param>
/// <param name="Completed">The number of requests completed, when reported.</param>
/// <param name="Failed">The number of requests failed, when reported.</param>
/// <param name="RetryAfter">The provider's recommended delay before polling again.</param>
public sealed record ProviderBatchStatus(
    BaizeBatchState State,
    string? ProviderStatus = null,
    int? Total = null,
    int? Completed = null,
    int? Failed = null,
    TimeSpan? RetryAfter = null);
