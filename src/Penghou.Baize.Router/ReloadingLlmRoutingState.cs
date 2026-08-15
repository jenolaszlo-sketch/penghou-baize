using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;
using System.Diagnostics;

namespace Penghou.Baize.Router;

/// <summary>
/// Owns the immutable routing runtime used by both <see cref="ILlmRouter"/>
/// and <see cref="ILlmModelLookup"/>. Configuration reloads build a complete
/// replacement before one atomic swap, so requests cannot observe a lookup
/// and strategy table from different configuration versions.
/// </summary>
internal sealed class ReloadingLlmRoutingState :
    ILlmRouter,
    ILlmModelLookup,
    ILlmEndpointValidator,
    IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILlmRouterMemory _memory;
    private readonly ILlmEndpointSelectionPolicy _selectionPolicy;
    private readonly ILogger _logger;
    private readonly IDisposable? _subscription;
    private volatile RoutingRuntimeSnapshot _current;
    private long _reloadVersion;
    private int _disposed;

    public ReloadingLlmRoutingState(
        IOptionsMonitor<LlmRoutingOptions> options,
        IServiceProvider services,
        ILlmRouterMemory memory,
        ILlmEndpointSelectionPolicy selectionPolicy,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _selectionPolicy = selectionPolicy ??
            throw new ArgumentNullException(nameof(selectionPolicy));
        _logger = logger ?? NullLogger.Instance;
        _current = Build(options.CurrentValue);
        _logger.LogInformation(
            "Initialized Baize routing snapshot with {EndpointCount} endpoint(s)",
            _current.Endpoints.Count);
        _subscription = options.OnChange(OnOptionsChanged);
    }

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default) =>
        _current.Router.StreamAsync(model, builder, cancellationToken);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        LlmRequest request,
        CancellationToken cancellationToken = default) =>
        _current.Router.StreamAsync(model, request, cancellationToken);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default) =>
        _current.Router.StreamAsync(strategy, builder, cancellationToken);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        LlmRequest request,
        CancellationToken cancellationToken = default) =>
        _current.Router.StreamAsync(strategy, request, cancellationToken);

    public IAsyncEnumerable<LlmStreamEvent> StreamRouteAsync(
        string route,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default) =>
        _current.Router.StreamRouteAsync(route, builder, cancellationToken);

    public IAsyncEnumerable<LlmStreamEvent> StreamRouteAsync(
        string route,
        LlmRequest request,
        CancellationToken cancellationToken = default) =>
        _current.Router.StreamRouteAsync(route, request, cancellationToken);

    [Obsolete("Use ResolveAsync to avoid blocking asynchronous router memory.")]
    public ResolvedEndpoint Resolve(string model) =>
        ResolveAsync(model).GetAwaiter().GetResult();

    public Task<ResolvedEndpoint> ResolveAsync(
        string model,
        CancellationToken cancellationToken = default) =>
        _current.Router.ResolveAsync(model, cancellationToken);

    [Obsolete("Use ResolveAsync to avoid blocking asynchronous router memory.")]
    public ResolvedEndpoint Resolve(ModelStrategy strategy) =>
        ResolveAsync(strategy).GetAwaiter().GetResult();

    public Task<ResolvedEndpoint> ResolveAsync(
        ModelStrategy strategy,
        CancellationToken cancellationToken = default) =>
        _current.Router.ResolveAsync(strategy, cancellationToken);

    public Task<ResolvedEndpoint> ResolveRouteAsync(
        string route,
        CancellationToken cancellationToken = default) =>
        _current.Router.ResolveRouteAsync(route, cancellationToken);

    public Task<LlmRouteExplanation> ExplainModelAsync(
        string model,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        _current.Router.ExplainModelAsync(model, request, cancellationToken);

    public Task<LlmRouteExplanation> ExplainStrategyAsync(
        ModelStrategy strategy,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        _current.Router.ExplainStrategyAsync(strategy, request, cancellationToken);

    public Task<LlmRouteExplanation> ExplainRouteAsync(
        string route,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        _current.Router.ExplainRouteAsync(route, request, cancellationToken);

    public ILlmClient GetClient(string model) => _current.Lookup.GetClient(model);

    public bool TryGetClient(string model, out ILlmClient client) =>
        _current.Lookup.TryGetClient(model, out client);

    public ILlmClient GetClient(string model, ApiStyle apiStyle) =>
        _current.Lookup.GetClient(model, apiStyle);

    public bool TryGetClient(
        string model,
        ApiStyle apiStyle,
        out ILlmClient client) =>
        _current.Lookup.TryGetClient(model, apiStyle, out client);

    public ILlmClient GetClient(string model, LlmProviderKey provider) =>
        _current.Lookup.GetClient(model, provider);

    public bool TryGetClient(
        string model,
        LlmProviderKey provider,
        out ILlmClient client) =>
        _current.Lookup.TryGetClient(model, provider, out client);

    public ILlmClient GetClientByEndpointId(string endpointId) =>
        _current.Lookup.GetClientByEndpointId(endpointId);

    public bool TryGetClientByEndpointId(
        string endpointId,
        out ILlmClient client) =>
        _current.Lookup.TryGetClientByEndpointId(endpointId, out client);

    public IBaizeBatchClient GetBatchClientByEndpointId(string endpointId) =>
        _current.Lookup.GetBatchClientByEndpointId(endpointId);

    public bool TryGetBatchClientByEndpointId(
        string endpointId,
        out IBaizeBatchClient client) =>
        _current.Lookup.TryGetBatchClientByEndpointId(endpointId, out client);

    public IReadOnlyList<ApiStyle> GetApiStyles(string model) =>
        _current.Lookup.GetApiStyles(model);

    public IReadOnlyList<LlmProviderKey> GetProviders(string model) =>
        _current.Lookup.GetProviders(model);

    public IReadOnlyList<ResolvedEndpoint> GetEndpoints(string model) =>
        _current.Lookup.GetEndpoints(model);

    public async Task<LlmEndpointValidationReport> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = _current;
        var tasks = snapshot.Endpoints.Select(endpoint =>
            ValidateEndpointAsync(endpoint, cancellationToken));
        return new LlmEndpointValidationReport(await Task.WhenAll(tasks));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _subscription?.Dispose();
    }

    private void OnOptionsChanged(LlmRoutingOptions options, string? name)
    {
        if (name != Options.DefaultName)
            return;

        var version = Interlocked.Increment(ref _reloadVersion);
        if (!ServiceCollectionExtensions.TryValidate(options, out var validationError))
        {
            RouterTelemetry.ConfigurationReloadFailures.Add(
                1,
                new KeyValuePair<string, object?>(
                    "error.type",
                    "ConfigurationValidation"));
            _logger.LogError(
                "Rejected invalid Baize routing snapshot version " +
                "{ReloadVersion}; the previous snapshot remains active. " +
                "Validation error: {ValidationError}",
                version,
                validationError);
            return;
        }

        try
        {
            var replacement = Build(options);
            if (version != Volatile.Read(ref _reloadVersion))
                return;

            _current = replacement;
            RouterTelemetry.ConfigurationReloads.Add(1);
            _logger.LogInformation(
                "Reloaded Baize routing snapshot version {ReloadVersion} with " +
                "{EndpointCount} endpoint(s)",
                version,
                replacement.Endpoints.Count);
        }
        catch (Exception exception)
        {
            RouterTelemetry.ConfigurationReloadFailures.Add(
                1,
                new KeyValuePair<string, object?>(
                    "error.type",
                    exception.GetType().Name));
            _logger.LogError(
                "Failed to build Baize routing snapshot version {ReloadVersion}; " +
                "the previous snapshot remains active. Error type {ErrorType}",
                version,
                exception.GetType().FullName);
        }
    }

    private RoutingRuntimeSnapshot Build(LlmRoutingOptions options)
    {
        ServiceCollectionExtensions.ValidateConfiguration(options);
        var built = ServiceCollectionExtensions.BuildRoutingLookup(
            _services,
            options);
        var strategies = options.StrategyFallbacks.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.AsReadOnly());
        var namedRoutes = options.NamedRoutes.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(),
            StringComparer.Ordinal);
        var customRouteProvider = _services.GetService<ILlmRouteProvider>();
        var router = customRouteProvider is null
            ? new LlmRouter(
                built.Lookup,
                strategies,
                namedRoutes,
                _memory,
                options.MaxPendingRequests,
                options.RequestTimeout,
                _selectionPolicy,
                options.Retry)
            : new LlmRouter(
                built.Lookup,
                customRouteProvider,
                _memory,
                options.MaxPendingRequests,
                options.RequestTimeout,
                options.Retry);
        return new RoutingRuntimeSnapshot(
            built.Lookup,
            router,
            built.Endpoints);
    }

    private async Task<LlmEndpointValidationResult> ValidateEndpointAsync(
        DeferredEndpointRuntime endpoint,
        CancellationToken cancellationToken)
    {
        using var activity = BaizeTelemetry.Activities.StartActivity(
            "llm.endpoint.validate",
            ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "endpoint_validate");
        activity?.SetTag("gen_ai.provider.name", endpoint.Provider);
        activity?.SetTag("gen_ai.request.model", endpoint.Model);
        activity?.SetTag("baize.endpoint.id", endpoint.EndpointId);
        var tags = new TagList
        {
            { "gen_ai.provider.name", endpoint.Provider },
            { "gen_ai.request.model", endpoint.Model },
            { "baize.endpoint.id", endpoint.EndpointId }
        };

        try
        {
            _logger.LogDebug(
                "Validating Baize endpoint {EndpointId}, provider {Provider}, " +
                "model {Model}",
                endpoint.EndpointId,
                endpoint.Provider,
                endpoint.Model);
            await endpoint.Clients.GetChatClientAsync(cancellationToken);
            if (endpoint.HasNativeBatch)
                await endpoint.Clients.GetBatchClientAsync(cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogInformation(
                "Validated Baize endpoint {EndpointId}, provider {Provider}, " +
                "model {Model}",
                endpoint.EndpointId,
                endpoint.Provider,
                endpoint.Model);
            return new LlmEndpointValidationResult(
                endpoint.EndpointId,
                endpoint.Provider,
                endpoint.Model,
                Succeeded: true);
        }
        catch (Exception exception) when
            (exception is not OperationCanceledException ||
             !cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            tags.Add("error.type", exception.GetType().Name);
            _logger.LogWarning(
                "Baize endpoint validation failed for {EndpointId}, provider " +
                "{Provider}, model {Model}, error type {ErrorType}",
                endpoint.EndpointId,
                endpoint.Provider,
                endpoint.Model,
                exception.GetType().FullName);
            return new LlmEndpointValidationResult(
                endpoint.EndpointId,
                endpoint.Provider,
                endpoint.Model,
                Succeeded: false,
                exception.Message);
        }
        finally
        {
            RouterTelemetry.EndpointValidations.Add(1, tags);
        }
    }

    private sealed record RoutingRuntimeSnapshot(
        ILlmModelLookup Lookup,
        ILlmRouter Router,
        IReadOnlyList<DeferredEndpointRuntime> Endpoints);
}
