namespace Penghou.Baize.Router;

/// <summary>
/// Binds a provider batch client to a configured endpoint so submitted handles
/// remain resumable through the endpoint-keyed router lookup.
/// </summary>
internal sealed class EndpointBoundBatchClient(
    string endpointId,
    IBaizeBatchClient inner)
    : IBaizeBatchClient
{
    public string ProviderId => inner.ProviderId;

    public BatchCapabilities Capabilities => inner.Capabilities;

    public async Task<ProviderBatchHandle> SubmitAsync(
        IReadOnlyList<BaizeBatchItem> items,
        BatchSubmissionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var handle = await inner.SubmitAsync(items, options, cancellationToken);
        return handle with { EndpointId = endpointId };
    }

    public Task<ProviderBatchStatus> GetStatusAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(handle);
        return inner.GetStatusAsync(handle, cancellationToken);
    }

    public Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(handle);
        return inner.GetResultsAsync(handle, cancellationToken);
    }

    public Task CancelAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(handle);
        return inner.CancelAsync(handle, cancellationToken);
    }

    private void ValidateEndpoint(ProviderBatchHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (handle.EndpointId is not null &&
            !string.Equals(handle.EndpointId, endpointId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Batch handle belongs to endpoint '{handle.EndpointId}', not '{endpointId}'.",
                nameof(handle));
        }
    }
}
