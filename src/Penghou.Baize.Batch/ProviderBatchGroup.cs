namespace Penghou.Baize.Batch;

/// <summary>
/// One physical provider batch within a <see cref="BatchPlan"/>: the requests
/// routed to a single endpoint, in submission order.
/// </summary>
/// <param name="EndpointId">The configured endpoint the group is submitted through.</param>
/// <param name="ProviderId">The provider adapter that owns the endpoint.</param>
/// <param name="Model">The registered model name, when the group was resolved from one.</param>
/// <param name="Items">The requests in the group, each with its stable correlation identifier.</param>
public sealed record ProviderBatchGroup(
    string EndpointId,
    string ProviderId,
    string? Model,
    IReadOnlyList<BaizeBatchItem> Items);
