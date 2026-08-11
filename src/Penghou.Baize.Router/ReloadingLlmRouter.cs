using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;

namespace Penghou.Baize.Router;

/// <summary>
/// <see cref="ILlmRouter"/> that rebuilds its routing table whenever
/// <see cref="IOptionsMonitor{LlmRoutingOptions}"/> reports a change, so
/// configuration edits (model maps, fallback chains, concurrency bounds)
/// apply without a restart. Dispose releases the options subscription.
/// </summary>
public sealed class ReloadingLlmRouter : ILlmRouter, IDisposable
{
    private readonly ILlmModelLookup _lookup;
    private readonly ILlmRouterMemory _memory;
    private readonly ILlmEndpointSelectionPolicy? _selectionPolicy;
    private readonly IOptionsMonitor<LlmRoutingOptions> _options;
    private readonly IDisposable? _subscription;
    private readonly IDisposable? _ownedLookup;
    private volatile LlmRouter _inner;

    /// <summary>Initializes a router that tracks an options monitor.</summary>
    /// <param name="options">The options to build the routing table from and reload on change.</param>
    /// <param name="services">The service provider used to build client factories.</param>
    /// <param name="memory">The router memory to record and consult.</param>
    public ReloadingLlmRouter(
        IOptionsMonitor<LlmRoutingOptions> options,
        IServiceProvider services,
        ILlmRouterMemory memory)
        : this(
            options,
            new ReloadingLlmModelLookup(options, services),
            memory,
            services.GetService<ILlmEndpointSelectionPolicy>(),
            ownsLookup: true)
    {
    }

    /// <summary>
    /// Initializes a router over a shared reloading model lookup. This is the
    /// DI path used by <c>AddLlmRouting</c>, ensuring lookup and router calls
    /// observe the same endpoint snapshot.
    /// </summary>
    public ReloadingLlmRouter(
        IOptionsMonitor<LlmRoutingOptions> options,
        ILlmModelLookup lookup,
        ILlmRouterMemory memory,
        ILlmEndpointSelectionPolicy? selectionPolicy = null)
        : this(options, lookup, memory, selectionPolicy, ownsLookup: false)
    {
    }

    private ReloadingLlmRouter(
        IOptionsMonitor<LlmRoutingOptions> options,
        ILlmModelLookup lookup,
        ILlmRouterMemory memory,
        ILlmEndpointSelectionPolicy? selectionPolicy,
        bool ownsLookup)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _selectionPolicy = selectionPolicy;
        _ownedLookup = ownsLookup ? lookup as IDisposable : null;

        _inner = Build(options.CurrentValue);
        _subscription = options.OnChange(OnOptionsChanged);
    }

    /// <summary>
    /// Streams a completion for a model, using the endpoint the router would
    /// currently pick for that model.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="builder">Builds the request for the stream.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The canonical stream events.</returns>
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default)
        => _inner.StreamAsync(model, builder, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        LlmRequest request,
        CancellationToken cancellationToken = default)
        => _inner.StreamAsync(model, request, cancellationToken);

    /// <summary>
    /// Streams a completion for a strategy, using the endpoint the router
    /// would currently pick from the strategy's fallback chain.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <param name="builder">Builds the request for the stream.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The canonical stream events.</returns>
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default)
        => _inner.StreamAsync(strategy, builder, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        LlmRequest request,
        CancellationToken cancellationToken = default)
        => _inner.StreamAsync(strategy, request, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<LlmStreamEvent> StreamRouteAsync(
        string route,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default) =>
        _inner.StreamRouteAsync(route, builder, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<LlmStreamEvent> StreamRouteAsync(
        string route,
        LlmRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.StreamRouteAsync(route, request, cancellationToken);

    /// <summary>
    /// The endpoint the router would currently use for a model, chosen from
    /// the model's configured endpoints by least-failing history.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The resolved endpoint.</returns>
    [Obsolete("Use ResolveAsync to avoid blocking asynchronous router memory.")]
    public ResolvedEndpoint Resolve(string model) =>
        ResolveAsync(model).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ResolvedEndpoint> ResolveAsync(
        string model,
        CancellationToken cancellationToken = default) =>
        _inner.ResolveAsync(model, cancellationToken);

    /// <summary>
    /// The endpoint the router would currently use for a strategy, chosen
    /// from the fallback chain's endpoints by least-failing history.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <returns>The resolved endpoint.</returns>
    [Obsolete("Use ResolveAsync to avoid blocking asynchronous router memory.")]
    public ResolvedEndpoint Resolve(ModelStrategy strategy) =>
        ResolveAsync(strategy).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<ResolvedEndpoint> ResolveAsync(
        ModelStrategy strategy,
        CancellationToken cancellationToken = default) =>
        _inner.ResolveAsync(strategy, cancellationToken);

    /// <inheritdoc />
    public Task<ResolvedEndpoint> ResolveRouteAsync(
        string route,
        CancellationToken cancellationToken = default) =>
        _inner.ResolveRouteAsync(route, cancellationToken);

    /// <summary>Releases the options subscription.</summary>
    public void Dispose()
    {
        _subscription?.Dispose();
        _ownedLookup?.Dispose();
    }

    private void OnOptionsChanged(LlmRoutingOptions options, string? name)
    {
        if (name != Options.DefaultName)
            return;

        _inner = Build(options);
    }

    private LlmRouter Build(LlmRoutingOptions options)
    {
        ServiceCollectionExtensions.ValidateConfiguration(options);
        var strategyLookup = options.StrategyFallbacks.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.AsReadOnly());
        var namedRoutes = options.NamedRoutes.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.AsReadOnly(),
            StringComparer.Ordinal);

        return new LlmRouter(
            _lookup,
            strategyLookup,
            namedRoutes,
            _memory,
            maxPendingRequests: options.MaxPendingRequests,
            requestTimeout: options.RequestTimeout,
            selectionPolicy: _selectionPolicy);
    }
}
