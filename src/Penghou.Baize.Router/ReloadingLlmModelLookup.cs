using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;

namespace Penghou.Baize.Router;

/// <summary>
/// <see cref="ILlmModelLookup"/> that rebuilds its routing table whenever
/// <see cref="IOptionsMonitor{LlmRoutingOptions}"/> reports a change, so the
/// lookup stays in sync with the reloading router. Dispose releases the
/// options subscription.
/// </summary>
public sealed class ReloadingLlmModelLookup : ILlmModelLookup, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<LlmRoutingOptions> _options;
    private readonly IDisposable? _subscription;
    private volatile ILlmModelLookup _inner;

    /// <summary>Initializes a lookup that tracks an options monitor.</summary>
    /// <param name="options">The options to build the lookup from and reload on change.</param>
    /// <param name="services">The service provider used to build client factories.</param>
    public ReloadingLlmModelLookup(
        IOptionsMonitor<LlmRoutingOptions> options,
        IServiceProvider services)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _services = services ?? throw new ArgumentNullException(nameof(services));

        _inner = Build(options.CurrentValue);
        _subscription = options.OnChange(OnOptionsChanged);
    }

    /// <summary>Returns the client for a model's first registered endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's default client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the model is not registered.</exception>
    public ILlmClient GetClient(string model) => _inner.GetClient(model);

    /// <summary>Tries to return the client for a model's first registered endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="client">The model's default client when found.</param>
    /// <returns><c>true</c> when the model is registered; otherwise <c>false</c>.</returns>
    public bool TryGetClient(string model, out ILlmClient client) =>
        _inner.TryGetClient(model, out client);

    /// <summary>Returns the client for a specific (model, API style) endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="apiStyle">The wire protocol of the endpoint.</param>
    /// <returns>The matching client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the endpoint is not registered.</exception>
    public ILlmClient GetClient(string model, ApiStyle apiStyle) =>
        _inner.GetClient(model, apiStyle);

    /// <summary>Tries to return the client for a specific (model, API style) endpoint.</summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="apiStyle">The wire protocol of the endpoint.</param>
    /// <param name="client">The matching client when found.</param>
    /// <returns><c>true</c> when the endpoint is registered; otherwise <c>false</c>.</returns>
    public bool TryGetClient(string model, ApiStyle apiStyle, out ILlmClient client) =>
        _inner.TryGetClient(model, apiStyle, out client);

    /// <inheritdoc />
    public ILlmClient GetClient(string model, LlmProviderKey provider) =>
        _inner.GetClient(model, provider);

    /// <inheritdoc />
    public bool TryGetClient(
        string model,
        LlmProviderKey provider,
        out ILlmClient client) =>
        _inner.TryGetClient(model, provider, out client);

    /// <summary>Returns the client for a specific endpoint id.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <returns>The matching client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the endpoint is not registered.</exception>
    public ILlmClient GetClientByEndpointId(string endpointId) =>
        _inner.GetClientByEndpointId(endpointId);

    /// <summary>Tries to return the client for a specific endpoint id.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="client">The matching client when found.</param>
    /// <returns><c>true</c> when the endpoint is registered; otherwise <c>false</c>.</returns>
    public bool TryGetClientByEndpointId(string endpointId, out ILlmClient client) =>
        _inner.TryGetClientByEndpointId(endpointId, out client);

    /// <inheritdoc />
    public IBaizeBatchClient GetBatchClientByEndpointId(string endpointId) =>
        _inner.GetBatchClientByEndpointId(endpointId);

    /// <inheritdoc />
    public bool TryGetBatchClientByEndpointId(
        string endpointId,
        out IBaizeBatchClient client) =>
        _inner.TryGetBatchClientByEndpointId(endpointId, out client);

    /// <summary>
    /// The API styles a model can be reached through, in registration order.
    /// Returns an empty list for unknown models.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's API styles in registration order.</returns>
    public IReadOnlyList<ApiStyle> GetApiStyles(string model) => _inner.GetApiStyles(model);

    /// <inheritdoc />
    public IReadOnlyList<LlmProviderKey> GetProviders(string model) =>
        _inner.GetProviders(model);

    /// <summary>
    /// The endpoints a model can be reached through, in registration order.
    /// Returns an empty list for unknown models.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The model's endpoints in registration order.</returns>
    public IReadOnlyList<ResolvedEndpoint> GetEndpoints(string model) => _inner.GetEndpoints(model);

    /// <summary>Releases the options subscription.</summary>
    public void Dispose() => _subscription?.Dispose();

    private void OnOptionsChanged(LlmRoutingOptions options, string? name)
    {
        if (name != Options.DefaultName)
            return;

        _inner = Build(options);
    }

    private ILlmModelLookup Build(LlmRoutingOptions options)
    {
        ServiceCollectionExtensions.ValidateConfiguration(options);
        return ServiceCollectionExtensions.BuildLookup(_services, options);
    }
}
