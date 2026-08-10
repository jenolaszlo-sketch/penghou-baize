using Penghou.Baize.Router;

namespace Penghou.Baize.Batch;

/// <summary>
/// Resolves batch clients from the router's reloadable endpoint lookup.
/// </summary>
public sealed class ModelLookupBatchClientResolver(
    ILlmModelLookup modelLookup)
    : IBaizeBatchClientResolver
{
    /// <inheritdoc />
    public IBaizeBatchClient GetClient(string endpointId) =>
        modelLookup.GetBatchClientByEndpointId(endpointId);

    /// <inheritdoc />
    public bool TryGetClient(
        string endpointId,
        out IBaizeBatchClient client) =>
        modelLookup.TryGetBatchClientByEndpointId(endpointId, out client);
}
