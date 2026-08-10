namespace Penghou.Baize.Router;

/// <summary>Default immutable provider registry.</summary>
public sealed class LlmClientProviderRegistry : ILlmClientProviderRegistry
{
    private readonly IReadOnlyDictionary<LlmProviderKey, ILlmClientProvider> _providers;

    /// <summary>Builds a registry from all DI-registered providers.</summary>
    public LlmClientProviderRegistry(IEnumerable<ILlmClientProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var result = new Dictionary<LlmProviderKey, ILlmClientProvider>();

        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);

            if (!result.TryAdd(provider.Key, provider))
            {
                throw new InvalidOperationException(
                    $"More than one LLM client provider is registered for key " +
                    $"'{provider.Key}'. Provider keys must be unique.");
            }
        }

        _providers = result;
        Keys = result.Keys.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<LlmProviderKey> Keys { get; }

    /// <inheritdoc />
    public ILlmClientProvider GetRequiredProvider(LlmProviderKey key)
    {
        if (_providers.TryGetValue(key, out var provider))
            return provider;

        var available = Keys.Count == 0
            ? "none"
            : string.Join(", ", Keys.OrderBy(candidate => candidate.Value));

        throw new InvalidOperationException(
            $"No LLM client provider is registered for key '{key}'. " +
            $"Registered providers: {available}. Install and register the " +
            "provider package explicitly, or declare its assembly under " +
            "LlmRouting:ProviderModules.");
    }
}
