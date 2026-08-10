using Penghou.Baize.Router;

namespace Penghou.Baize.Batch;

/// <summary>
/// Default <see cref="IBaizeBatchClientResolver"/> backed by endpoint-keyed
/// client factories. Instances are created lazily per endpoint, mirroring
/// <see cref="LlmModelLookup"/>.
/// </summary>
public sealed class BatchClientResolver : IBaizeBatchClientResolver
{
    private readonly IReadOnlyDictionary<string, Func<IBaizeBatchClient>> _byEndpointId;

    /// <summary>Initializes a resolver from endpoint-keyed factories.</summary>
    /// <param name="byEndpointId">The endpoint id to batch client factory mapping.</param>
    public BatchClientResolver(
        IReadOnlyDictionary<string, Func<IBaizeBatchClient>> byEndpointId)
    {
        _byEndpointId = byEndpointId ??
            new Dictionary<string, Func<IBaizeBatchClient>>();
    }

    /// <summary>Initializes a resolver from concrete endpoint-keyed clients.</summary>
    /// <param name="clients">The endpoint id to batch client mapping.</param>
    public BatchClientResolver(
        IReadOnlyDictionary<string, IBaizeBatchClient> clients)
        : this(clients.ToDictionary(
            pair => pair.Key,
            pair => new Func<IBaizeBatchClient>(() => pair.Value),
            StringComparer.Ordinal))
    {
    }

    /// <inheritdoc />
    public IBaizeBatchClient GetClient(string endpointId)
    {
        if (!TryGetClient(endpointId, out var client))
        {
            throw new KeyNotFoundException(
                $"No batch client registered for endpoint id '{endpointId}'.");
        }

        return client;
    }

    /// <inheritdoc />
    public bool TryGetClient(string endpointId, out IBaizeBatchClient client)
    {
        if (_byEndpointId.TryGetValue(endpointId, out var factory))
        {
            client = factory();
            return true;
        }

        client = null!;
        return false;
    }
}
