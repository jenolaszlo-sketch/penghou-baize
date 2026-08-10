namespace Penghou.Baize;

/// <summary>
/// A serializable reference to a provider-side batch operation. Provider batch
/// clients are stateless; the handle contains everything required to reconnect
/// to the operation from another process or machine, so an orchestration runtime
/// can persist it and resume polling later.
/// </summary>
/// <param name="ProviderId">The provider adapter that owns the batch.</param>
/// <param name="BatchId">The provider-assigned batch identifier.</param>
/// <param name="EndpointId">The configured endpoint the batch was submitted through, when known.</param>
/// <param name="Metadata">Provider-specific submission metadata echoed back for diagnostics, when any.</param>
public sealed record ProviderBatchHandle(
    string ProviderId,
    string BatchId,
    string? EndpointId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
