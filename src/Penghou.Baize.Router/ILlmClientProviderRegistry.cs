namespace Penghou.Baize.Router;

/// <summary>Resolves provider adapters registered with dependency injection.</summary>
public interface ILlmClientProviderRegistry
{
    /// <summary>The registered provider keys.</summary>
    IReadOnlyCollection<LlmProviderKey> Keys { get; }

    /// <summary>Returns the provider registered for <paramref name="key"/>.</summary>
    /// <exception cref="InvalidOperationException">No provider is registered for the key.</exception>
    ILlmClientProvider GetRequiredProvider(LlmProviderKey key);
}
