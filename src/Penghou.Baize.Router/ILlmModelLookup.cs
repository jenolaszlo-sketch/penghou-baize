namespace Penghou.Baize.Router;

/// <summary>
/// Resolves registered model names to concrete <see cref="ILlmClient"/>
/// instances, either by name alone (first registered endpoint), by the
/// (name, API style) pair, or by the endpoint's stable id
/// (<see cref="ResolvedEndpoint.EndpointId"/>).
/// </summary>
public interface ILlmModelLookup
{
    /// <summary>Returns the client for a model's first registered endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's default client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the model is not registered.</exception>
    ILlmClient GetClient(string model);

    /// <summary>Tries to return the client for a model's first registered endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="client">The model's default client when found.</param>
    /// <returns><c>true</c> when the model is registered; otherwise <c>false</c>.</returns>
    bool TryGetClient(string model, out ILlmClient client);

    /// <summary>Returns the client for a specific (model, API style) endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="apiStyle">The wire protocol of the endpoint.</param>
    /// <returns>The matching client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the endpoint is not registered.</exception>
    ILlmClient GetClient(string model, ApiStyle apiStyle);

    /// <summary>Tries to return the client for a specific (model, API style) endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="apiStyle">The wire protocol of the endpoint.</param>
    /// <param name="client">The matching client when found.</param>
    /// <returns><c>true</c> when the endpoint is registered; otherwise <c>false</c>.</returns>
    bool TryGetClient(string model, ApiStyle apiStyle, out ILlmClient client);

    /// <summary>Returns the client for a specific extensible provider.</summary>
    ILlmClient GetClient(string model, LlmProviderKey provider)
    {
        if (provider.TryGetApiStyle(out var apiStyle))
            return GetClient(model, apiStyle);

        throw new KeyNotFoundException(
            $"No client is registered for model '{model}' and provider '{provider}'.");
    }

    /// <summary>Tries to return the client for a specific extensible provider.</summary>
    bool TryGetClient(string model, LlmProviderKey provider, out ILlmClient client)
    {
        if (provider.TryGetApiStyle(out var apiStyle))
            return TryGetClient(model, apiStyle, out client);

        client = null!;
        return false;
    }

    /// <summary>Returns the client for a specific endpoint id.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <returns>The matching client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the endpoint is not registered.</exception>
    ILlmClient GetClientByEndpointId(string endpointId);

    /// <summary>Tries to return the client for a specific endpoint id.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="client">The matching client when found.</param>
    /// <returns><c>true</c> when the endpoint is registered; otherwise <c>false</c>.</returns>
    bool TryGetClientByEndpointId(string endpointId, out ILlmClient client);

    /// <summary>Returns the native batch client for a specific endpoint id.</summary>
    IBaizeBatchClient GetBatchClientByEndpointId(string endpointId);

    /// <summary>Tries to return the native batch client for a specific endpoint id.</summary>
    bool TryGetBatchClientByEndpointId(
        string endpointId,
        out IBaizeBatchClient client);

    /// <summary>
    /// The API styles a model can be reached through, in registration order.
    /// Returns an empty list for unknown models.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's API styles in registration order.</returns>
    IReadOnlyList<ApiStyle> GetApiStyles(string model);

    /// <summary>The provider keys a model can be reached through.</summary>
    IReadOnlyList<LlmProviderKey> GetProviders(string model) =>
        GetApiStyles(model).Select(style => style.ToProviderKey()).ToArray();

    /// <summary>
    /// The endpoints a model can be reached through, in registration order.
    /// Returns an empty list for unknown models.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's endpoints in registration order.</returns>
    IReadOnlyList<ResolvedEndpoint> GetEndpoints(string model);
}
