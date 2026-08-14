using System.Collections.Concurrent;

namespace Penghou.Baize.Generation;

/// <summary>The default in-memory <see cref="IGenerationClientRegistry"/>.</summary>
public sealed class DefaultGenerationClientRegistry : IGenerationClientRegistry
{
    private readonly ConcurrentDictionary<EndpointKey, IGenerationClient> _clients =
        new();

    /// <inheritdoc />
    public void Register(string provider, string endpointId, IGenerationClient client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(client);
        _clients[new EndpointKey(provider, endpointId)] = client;
    }

    /// <inheritdoc />
    public IGenerationClient? Find(string provider, string endpointId) =>
        _clients.TryGetValue(new EndpointKey(provider, endpointId), out var client)
            ? client
            : null;

    /// <inheritdoc />
    public IReadOnlyList<GenerationEndpoint> Endpoints =>
        _clients
            .OrderBy(pair => pair.Key.Provider, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.EndpointId, StringComparer.Ordinal)
            .Select(pair => new GenerationEndpoint(
                pair.Key.Provider,
                pair.Key.EndpointId,
                pair.Value))
            .ToArray();

    private sealed record EndpointKey(string Provider, string EndpointId);
}