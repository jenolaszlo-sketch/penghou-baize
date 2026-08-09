namespace Penghou.Baize.Router;

/// <summary>
/// Default <see cref="ILlmModelLookup"/> backed by dictionaries of client
/// factories. Endpoints are addressed by their stable id (see
/// <see cref="ResolvedEndpoint.EndpointId"/>), while the plain-name and
/// (model, API style) accessors return the first matching registered endpoint.
/// </summary>
public sealed class LlmModelLookup : ILlmModelLookup
{
    private readonly IReadOnlyDictionary<string, Func<ILlmClient>> _defaults;
    private readonly IReadOnlyDictionary<string, Func<ILlmClient>> _byEndpointId;
    private readonly IReadOnlyDictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>> _byStyle;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ApiStyle>> _stylesByModel;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ResolvedEndpoint>> _endpointsByModel;

    /// <summary>Initializes a lookup.</summary>
    /// <param name="defaults">The per-model default client factories (first registered endpoint).</param>
    /// <param name="byStyle">The per-(model, API style) client factories for the first endpoint of each style.</param>
    /// <param name="stylesByModel">
    /// The per-model API styles in registration order; when omitted, derived
    /// from the keys of <paramref name="byStyle"/>.
    /// </param>
    /// <param name="byEndpointId">The per-endpoint-id client factories.</param>
    /// <param name="endpointsByModel">
    /// The per-model endpoints in registration order; when omitted, one
    /// endpoint per (model, API style) key of <paramref name="byStyle"/> is
    /// synthesized with the id <c>{model}:{apiStyle}</c>. Prefer supplying it
    /// when a model has several endpoints of the same style (for example a
    /// primary and a backup gateway) so each gets a distinct id.
    /// </param>
    public LlmModelLookup(
        IReadOnlyDictionary<string, Func<ILlmClient>> defaults,
        IReadOnlyDictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>> byStyle,
        IReadOnlyDictionary<string, IReadOnlyList<ApiStyle>>? stylesByModel = null,
        IReadOnlyDictionary<string, Func<ILlmClient>>? byEndpointId = null,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedEndpoint>>? endpointsByModel = null)
    {
        _defaults = defaults;
        _byStyle = byStyle;

        var styleKeys = byStyle.Keys.ToList();

        _stylesByModel = stylesByModel
            ?? styleKeys
                .GroupBy(key => key.Model)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<ApiStyle>)group
                        .Select(key => key.ApiStyle)
                        .ToList());

        var endpointIds = new Dictionary<string, Func<ILlmClient>>(StringComparer.Ordinal);
        var endpointsByModelResult =
            new Dictionary<string, List<ResolvedEndpoint>>(StringComparer.Ordinal);

        if (endpointsByModel is not null)
        {
            foreach (var (model, endpoints) in endpointsByModel)
                endpointsByModelResult[model] = endpoints.ToList();
        }
        else
        {
            foreach (var key in styleKeys)
            {
                var id = $"{key.Model}:{key.ApiStyle}";

                if (!endpointsByModelResult.TryGetValue(key.Model, out var endpoints))
                {
                    endpoints = new List<ResolvedEndpoint>();
                    endpointsByModelResult[key.Model] = endpoints;
                }

                endpoints.Add(new ResolvedEndpoint(id, key.Model, key.ApiStyle));
            }
        }

        _endpointsByModel = endpointsByModelResult.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<ResolvedEndpoint>)kv.Value,
            StringComparer.Ordinal);

        if (byEndpointId is not null)
        {
            foreach (var (id, factory) in byEndpointId)
                endpointIds[id] = factory;
        }
        else
        {
            foreach (var key in styleKeys)
            {
                var id = $"{key.Model}:{key.ApiStyle}";
                endpointIds[id] = byStyle[key];
            }
        }

        _byEndpointId = endpointIds;
    }

    /// <summary>Returns the client for a model's first registered endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's default client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the model is not registered.</exception>
    public ILlmClient GetClient(string model)
    {
        if (!TryGetClient(model, out var client))
            throw new KeyNotFoundException($"No client registered for model '{model}'.");

        return client;
    }

    /// <summary>Tries to return the client for a model's first registered endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="client">The model's default client when found.</param>
    /// <returns><c>true</c> when the model is registered; otherwise <c>false</c>.</returns>
    public bool TryGetClient(string model, out ILlmClient client)
    {
        if (_defaults.TryGetValue(model, out var factory))
        {
            client = factory();
            return true;
        }

        client = null!;
        return false;
    }

    /// <summary>Returns the client for a specific (model, API style) endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="apiStyle">The wire protocol of the endpoint.</param>
    /// <returns>The matching client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the endpoint is not registered.</exception>
    public ILlmClient GetClient(string model, ApiStyle apiStyle)
    {
        if (!TryGetClient(model, apiStyle, out var client))
            throw new KeyNotFoundException(
                $"No client registered for model '{model}' with API style '{apiStyle}'.");

        return client;
    }

    /// <summary>Tries to return the client for a specific (model, API style) endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="apiStyle">The wire protocol of the endpoint.</param>
    /// <param name="client">The matching client when found.</param>
    /// <returns><c>true</c> when the endpoint is registered; otherwise <c>false</c>.</returns>
    public bool TryGetClient(string model, ApiStyle apiStyle, out ILlmClient client)
    {
        if (_byStyle.TryGetValue((model, apiStyle), out var factory))
        {
            client = factory();
            return true;
        }

        client = null!;
        return false;
    }

    /// <summary>Returns the client for a specific endpoint id.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <returns>The matching client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the endpoint is not registered.</exception>
    public ILlmClient GetClientByEndpointId(string endpointId)
    {
        if (!TryGetClientByEndpointId(endpointId, out var client))
            throw new KeyNotFoundException(
                $"No client registered for endpoint id '{endpointId}'.");

        return client;
    }

    /// <summary>Tries to return the client for a specific endpoint id.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="client">The matching client when found.</param>
    /// <returns><c>true</c> when the endpoint is registered; otherwise <c>false</c>.</returns>
    public bool TryGetClientByEndpointId(string endpointId, out ILlmClient client)
    {
        if (_byEndpointId.TryGetValue(endpointId, out var factory))
        {
            client = factory();
            return true;
        }

        client = null!;
        return false;
    }

    /// <summary>
    /// The API styles a model can be reached through, in registration order.
    /// Returns an empty list for unknown models.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's API styles in registration order.</returns>
    public IReadOnlyList<ApiStyle> GetApiStyles(string model) =>
        _stylesByModel.TryGetValue(model, out var styles)
            ? styles
            : [];

    /// <summary>
    /// The endpoints a model can be reached through, in registration order.
    /// Returns an empty list for unknown models.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's endpoints in registration order.</returns>
    public IReadOnlyList<ResolvedEndpoint> GetEndpoints(string model) =>
        _endpointsByModel.TryGetValue(model, out var endpoints)
            ? endpoints
            : [];
}
