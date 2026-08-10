namespace Penghou.Baize.Router;

/// <summary>
/// Default <see cref="ILlmModelLookup"/> backed by dictionaries of client
/// factories. Provider keys are extensible; API-style members remain as
/// compatibility conveniences for the built-in adapters.
/// </summary>
public sealed class LlmModelLookup : ILlmModelLookup
{
    private readonly IReadOnlyDictionary<string, Func<ILlmClient>> _defaults;
    private readonly IReadOnlyDictionary<string, Func<ILlmClient>> _byEndpointId;
    private readonly IReadOnlyDictionary<(string Model, LlmProviderKey Provider), Func<ILlmClient>> _byProvider;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<LlmProviderKey>> _providersByModel;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ResolvedEndpoint>> _endpointsByModel;

    /// <summary>Initializes a lookup using extensible provider keys.</summary>
    public LlmModelLookup(
        IReadOnlyDictionary<string, Func<ILlmClient>> defaults,
        IReadOnlyDictionary<(string Model, LlmProviderKey Provider), Func<ILlmClient>> byProvider,
        IReadOnlyDictionary<string, IReadOnlyList<LlmProviderKey>>? providersByModel = null,
        IReadOnlyDictionary<string, Func<ILlmClient>>? byEndpointId = null,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedEndpoint>>? endpointsByModel = null)
    {
        _defaults = defaults;
        _byProvider = byProvider;

        var providerKeys = byProvider.Keys.ToList();
        _providersByModel = providersByModel
            ?? providerKeys
                .GroupBy(key => key.Model)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<LlmProviderKey>)group
                        .Select(key => key.Provider)
                        .ToList());

        var endpointIds = new Dictionary<string, Func<ILlmClient>>(StringComparer.Ordinal);
        var endpointGroups =
            new Dictionary<string, List<ResolvedEndpoint>>(StringComparer.Ordinal);

        if (endpointsByModel is not null)
        {
            foreach (var (model, endpoints) in endpointsByModel)
                endpointGroups[model] = endpoints.ToList();
        }
        else
        {
            foreach (var key in providerKeys)
            {
                var id = $"{key.Model}:{key.Provider}";
                if (!endpointGroups.TryGetValue(key.Model, out var endpoints))
                {
                    endpoints = [];
                    endpointGroups[key.Model] = endpoints;
                }

                endpoints.Add(new ResolvedEndpoint(id, key.Model, key.Provider));
            }
        }

        _endpointsByModel = endpointGroups.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ResolvedEndpoint>)pair.Value,
            StringComparer.Ordinal);

        if (byEndpointId is not null)
        {
            foreach (var (id, factory) in byEndpointId)
                endpointIds[id] = factory;
        }
        else
        {
            foreach (var key in providerKeys)
                endpointIds[$"{key.Model}:{key.Provider}"] = byProvider[key];
        }

        _byEndpointId = endpointIds;
    }

    /// <summary>Initializes a lookup using legacy built-in API styles.</summary>
    public LlmModelLookup(
        IReadOnlyDictionary<string, Func<ILlmClient>> defaults,
        IReadOnlyDictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>> byStyle,
        IReadOnlyDictionary<string, IReadOnlyList<ApiStyle>>? stylesByModel = null,
        IReadOnlyDictionary<string, Func<ILlmClient>>? byEndpointId = null,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedEndpoint>>? endpointsByModel = null)
        : this(
            defaults,
            byStyle.ToDictionary(
                pair => (pair.Key.Model, pair.Key.ApiStyle.ToProviderKey()),
                pair => pair.Value),
            stylesByModel?.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<LlmProviderKey>)pair.Value
                    .Select(style => style.ToProviderKey())
                    .ToList()),
            byEndpointId,
            endpointsByModel)
    {
    }

    /// <inheritdoc />
    public ILlmClient GetClient(string model)
    {
        if (!TryGetClient(model, out var client))
            throw new KeyNotFoundException($"No client registered for model '{model}'.");
        return client;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public ILlmClient GetClient(string model, ApiStyle apiStyle) =>
        GetClient(model, apiStyle.ToProviderKey());

    /// <inheritdoc />
    public bool TryGetClient(string model, ApiStyle apiStyle, out ILlmClient client) =>
        TryGetClient(model, apiStyle.ToProviderKey(), out client);

    /// <inheritdoc />
    public ILlmClient GetClient(string model, LlmProviderKey provider)
    {
        if (!TryGetClient(model, provider, out var client))
        {
            throw new KeyNotFoundException(
                $"No client registered for model '{model}' with provider '{provider}'.");
        }

        return client;
    }

    /// <inheritdoc />
    public bool TryGetClient(
        string model,
        LlmProviderKey provider,
        out ILlmClient client)
    {
        if (_byProvider.TryGetValue((model, provider), out var factory))
        {
            client = factory();
            return true;
        }

        client = null!;
        return false;
    }

    /// <inheritdoc />
    public ILlmClient GetClientByEndpointId(string endpointId)
    {
        if (!TryGetClientByEndpointId(endpointId, out var client))
        {
            throw new KeyNotFoundException(
                $"No client registered for endpoint id '{endpointId}'.");
        }

        return client;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public IReadOnlyList<ApiStyle> GetApiStyles(string model) =>
        GetProviders(model)
            .Select(provider => provider.TryGetApiStyle(out var style)
                ? (ApiStyle?)style
                : null)
            .Where(style => style.HasValue)
            .Select(style => style!.Value)
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<LlmProviderKey> GetProviders(string model) =>
        _providersByModel.TryGetValue(model, out var providers)
            ? providers
            : [];

    /// <inheritdoc />
    public IReadOnlyList<ResolvedEndpoint> GetEndpoints(string model) =>
        _endpointsByModel.TryGetValue(model, out var endpoints)
            ? endpoints
            : [];
}
