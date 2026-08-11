namespace Penghou.Baize.Router;

/// <summary>
/// Default route provider for direct model targets, built-in strategy chains,
/// and application-defined named routes.
/// </summary>
public sealed class ConfiguredLlmRouteProvider : LlmRouteProviderBase
{
    private readonly ILlmModelLookup _modelLookup;
    private readonly IReadOnlyDictionary<ModelStrategy, IReadOnlyList<string>> _strategies;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _namedRoutes;
    private readonly ILlmEndpointSelectionPolicy _selectionPolicy;

    /// <summary>Initializes the configured route provider.</summary>
    public ConfiguredLlmRouteProvider(
        ILlmModelLookup modelLookup,
        IReadOnlyDictionary<ModelStrategy, IReadOnlyList<string>> strategies,
        IReadOnlyDictionary<string, IReadOnlyList<string>> namedRoutes,
        ILlmRouterMemory memory,
        ILlmEndpointSelectionPolicy selectionPolicy)
        : base(memory)
    {
        _modelLookup = modelLookup ?? throw new ArgumentNullException(nameof(modelLookup));
        _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        _namedRoutes = namedRoutes ?? throw new ArgumentNullException(nameof(namedRoutes));
        _selectionPolicy = selectionPolicy ??
            throw new ArgumentNullException(nameof(selectionPolicy));
    }

    /// <inheritdoc />
    public override async ValueTask<LlmRouteResolution> ResolveAsync(
        LlmRoutingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Target);
        var models = ResolveModelChain(context.Target);
        var endpoints = models
            .SelectMany(_modelLookup.GetEndpoints)
            .ToArray();

        if (endpoints.Length == 0)
        {
            var kind = context.Target.Kind == LlmRouteKind.Model
                ? LlmRoutingFailureKind.ModelNotFound
                : LlmRoutingFailureKind.NoRegisteredEndpoint;
            throw new LlmRoutingException(
                context.Target.Kind == LlmRouteKind.Model
                    ? $"No client is registered for model '{context.Target.Name}'."
                    : $"No configured model for '{context.Target}' has a registered endpoint.",
                kind,
                context.Target,
                models);
        }

        var compatibility = EvaluateCompatibility(endpoints, context.Request);
        var stats = await Task.WhenAll(endpoints.Select(endpoint =>
            GetStatsAsync(endpoint, cancellationToken)));
        var statsById = stats.ToDictionary(
            value => value.EndpointId,
            StringComparer.Ordinal);
        var compatible = compatibility
            .Where(value => value.Compatible)
            .Select(value => value.Endpoint)
            .ToArray();

        if (compatible.Length == 0)
        {
            var rejected = ExplainCandidates(
                compatibility,
                statsById,
                new Dictionary<string, int>(StringComparer.Ordinal));
            throw new LlmRoutingException(
                "No configured endpoint satisfies the request capabilities.",
                LlmRoutingFailureKind.NoCompatibleEndpoint,
                context.Target,
                models,
                rejected);
        }

        var strategy = context.Target.Strategy ?? ModelStrategy.Auto;
        var ordered = await _selectionPolicy.OrderAsync(
            compatible,
            context.Request,
            strategy,
            Memory,
            cancellationToken);
        var ranks = ordered
            .Select((endpoint, rank) => (endpoint.EndpointId, rank))
            .ToDictionary(value => value.EndpointId, value => value.rank,
                StringComparer.Ordinal);
        var candidates = ExplainCandidates(compatibility, statsById, ranks);
        return new LlmRouteResolution(
            ordered,
            new LlmRouteExplanation(
                context.Target,
                models,
                candidates,
                ordered.FirstOrDefault()));
    }

    private IReadOnlyList<string> ResolveModelChain(LlmRouteTarget target) =>
        target.Kind switch
        {
            LlmRouteKind.Model => [target.Name!],
            LlmRouteKind.Strategy when
                target.Strategy is { } strategy &&
                _strategies.TryGetValue(strategy, out var chain) &&
                chain.Count > 0 => chain,
            LlmRouteKind.Named when
                _namedRoutes.TryGetValue(target.Name!, out var chain) &&
                chain.Count > 0 => chain,
            LlmRouteKind.Strategy => throw new LlmRoutingException(
                $"No models are configured for strategy '{target.Strategy}'.",
                LlmRoutingFailureKind.RouteNotFound,
                target),
            LlmRouteKind.Named => throw new LlmRoutingException(
                $"No models are configured for named route '{target.Name}'.",
                LlmRoutingFailureKind.RouteNotFound,
                target),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

    private IReadOnlyList<CandidateCompatibility> EvaluateCompatibility(
        IReadOnlyList<ResolvedEndpoint> endpoints,
        LlmRequest? request)
    {
        if (request is null)
        {
            return endpoints
                .Select(endpoint => new CandidateCompatibility(endpoint, true, null))
                .ToArray();
        }

        var requirements = LlmRequestRequirements.From(request);
        return endpoints.Select(endpoint =>
        {
            var capabilities = _modelLookup
                .GetClientByEndpointId(endpoint.EndpointId)
                .Capabilities;
            var compatible = requirements.IsSatisfiedBy(capabilities, out var reason);
            return new CandidateCompatibility(endpoint, compatible, reason);
        }).ToArray();
    }

    private static IReadOnlyList<LlmRouteCandidateExplanation> ExplainCandidates(
        IReadOnlyList<CandidateCompatibility> compatibility,
        IReadOnlyDictionary<string, LlmEndpointStats> stats,
        IReadOnlyDictionary<string, int> ranks) =>
        compatibility.Select(candidate => new LlmRouteCandidateExplanation(
            candidate.Endpoint,
            candidate.Compatible,
            candidate.Reason,
            ranks.GetValueOrDefault(candidate.Endpoint.EndpointId, -1) is var rank && rank >= 0
                ? rank
                : null,
            stats[candidate.Endpoint.EndpointId])).ToArray();

    private sealed record CandidateCompatibility(
        ResolvedEndpoint Endpoint,
        bool Compatible,
        string? Reason);
}
