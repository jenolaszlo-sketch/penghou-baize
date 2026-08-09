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
    private readonly IServiceProvider _services;
    private readonly ILlmRouterMemory _memory;
    private readonly IOptionsMonitor<LlmRoutingOptions> _options;
    private readonly IDisposable? _subscription;
    private volatile LlmRouter _inner;

    /// <summary>Initializes a router that tracks an options monitor.</summary>
    /// <param name="options">The options to build the routing table from and reload on change.</param>
    /// <param name="services">The service provider used to build client factories.</param>
    /// <param name="memory">The router memory to record and consult.</param>
    public ReloadingLlmRouter(
        IOptionsMonitor<LlmRoutingOptions> options,
        IServiceProvider services,
        ILlmRouterMemory memory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));

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

    /// <summary>
    /// The endpoint the router would currently use for a model, chosen from
    /// the model's configured endpoints by least-failing history.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The resolved endpoint.</returns>
    public ResolvedEndpoint Resolve(string model) => _inner.Resolve(model);

    /// <summary>
    /// The endpoint the router would currently use for a strategy, chosen
    /// from the fallback chain's endpoints by least-failing history.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <returns>The resolved endpoint.</returns>
    public ResolvedEndpoint Resolve(ModelStrategy strategy) => _inner.Resolve(strategy);

    /// <summary>Releases the options subscription.</summary>
    public void Dispose() => _subscription?.Dispose();

    private void OnOptionsChanged(LlmRoutingOptions options, string? name)
    {
        if (name != Options.DefaultName)
            return;

        _inner = Build(options);
    }

    private LlmRouter Build(LlmRoutingOptions options)
    {
        var lookup = ServiceCollectionExtensions.BuildLookup(_services, options);
        var strategyLookup = options.StrategyFallbacks.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.AsReadOnly());

        return new LlmRouter(
            lookup,
            strategyLookup,
            _memory,
            maxPendingRequests: options.MaxPendingRequests,
            requestTimeout: options.RequestTimeout);
    }
}
