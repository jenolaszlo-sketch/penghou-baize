namespace Penghou.Baize;

/// <summary>
/// Extensible provider adapter used by the router to construct clients without
/// referencing concrete provider packages.
/// </summary>
public interface ILlmClientProvider
{
    /// <summary>The unique provider key used in routing configuration.</summary>
    LlmProviderKey Key { get; }

    /// <summary>The provider's default endpoint URL.</summary>
    string DefaultBaseUrl { get; }

    /// <summary>Conservative capabilities guaranteed by the wire adapter.</summary>
    LlmEndpointCapabilities DefaultCapabilities { get; }

    /// <summary>Creates a client for a configured endpoint.</summary>
    /// <param name="context">The resolved provider-neutral endpoint context.</param>
    /// <returns>The configured client.</returns>
    ILlmClient CreateClient(LlmClientProviderContext context);

    /// <summary>
    /// Creates an asynchronous batch client for a configured endpoint, when the
    /// provider supports native batching. Returns null when it does not; the
    /// provider's conservative <see cref="LlmEndpointCapabilities.Batch"/> value
    /// must be consistent with this.
    /// </summary>
    /// <param name="context">The resolved provider-neutral endpoint context.</param>
    /// <returns>The configured batch client, or null when the provider has no batch adapter.</returns>
    IBaizeBatchClient? CreateBatchClient(LlmClientProviderContext context) => null;
}
