using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Router;

/// <summary>
/// Default <see cref="ILlmRouter"/>. Resolves a model or strategy to its
/// least-failing registered endpoint, skipping endpoints under a rate-limit
/// cooldown, records each call, and retries the same request against the next
/// endpoint in the fallback chain when an attempt fails before producing
/// meaningful output (content or tool-call deltas). Attempts share a single
/// per-request deadline. Each stream ends with a
/// <see cref="LlmRouterDiagnostics"/> event describing the attempts.
/// </summary>
/// <param name="modelLookup">Resolves model names and endpoints to clients.</param>
/// <param name="strategyLookup">The per-strategy fallback chains.</param>
/// <param name="memory">The router memory to record and consult; defaults to an in-memory implementation.</param>
/// <param name="maxPendingRequests">
/// The maximum number of in-flight streams; 0 (the default) means unbounded.
/// </param>
/// <param name="requestTimeout">
/// A per-request bound shared by every attempt; a stream exceeding it is
/// cancelled and recorded as an availability failure. Null (the default)
/// means no bound.
/// </param>
/// <param name="selectionPolicy">Ranks endpoints after capability filtering.</param>
/// <param name="retryOptions">Bounded retry behavior after transient route exhaustion.</param>
public class LlmRouter(
    ILlmModelLookup modelLookup,
    IReadOnlyDictionary<ModelStrategy, IReadOnlyList<string>> strategyLookup,
    ILlmRouterMemory? memory = null,
    int maxPendingRequests = 0,
    TimeSpan? requestTimeout = null,
    ILlmEndpointSelectionPolicy? selectionPolicy = null,
    LlmRouterRetryOptions? retryOptions = null) : ILlmRouter
{
    private readonly ILlmRouterMemory _memory = memory ?? new InMemoryLlmRouterMemory();
    private readonly SemaphoreSlim? _gate =
        maxPendingRequests > 0 ? new SemaphoreSlim(maxPendingRequests) : null;
    private readonly TimeSpan? _requestTimeout = requestTimeout;
    private readonly ILlmEndpointSelectionPolicy _selectionPolicy =
        selectionPolicy ?? new ReliabilityEndpointSelectionPolicy();
    private readonly LlmRouterRetryOptions _retryOptions =
        ValidateRetryOptions(retryOptions ?? LlmRouterRetryOptions.Default);
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _namedRouteLookup =
        new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.Ordinal);
    private ILlmRouteProvider? _routeProvider;

    /// <summary>
    /// Initializes a router with application-defined named fallback chains in
    /// addition to the built-in strategy chains.
    /// </summary>
    public LlmRouter(
        ILlmModelLookup modelLookup,
        IReadOnlyDictionary<ModelStrategy, IReadOnlyList<string>> strategyLookup,
        IReadOnlyDictionary<string, IReadOnlyList<string>> namedRouteLookup,
        ILlmRouterMemory? memory = null,
        int maxPendingRequests = 0,
        TimeSpan? requestTimeout = null,
        ILlmEndpointSelectionPolicy? selectionPolicy = null,
        LlmRouterRetryOptions? retryOptions = null)
        : this(
            modelLookup,
            strategyLookup,
            memory,
            maxPendingRequests,
            requestTimeout,
            selectionPolicy,
            retryOptions)
    {
        _namedRouteLookup = namedRouteLookup ??
            throw new ArgumentNullException(nameof(namedRouteLookup));
        _routeProvider = new ConfiguredLlmRouteProvider(
            modelLookup,
            strategyLookup,
            _namedRouteLookup,
            _memory,
            _selectionPolicy);
    }

    /// <summary>
    /// Initializes a router with a completely replaceable route provider.
    /// </summary>
    public LlmRouter(
        ILlmModelLookup modelLookup,
        ILlmRouteProvider routeProvider,
        ILlmRouterMemory? memory = null,
        int maxPendingRequests = 0,
        TimeSpan? requestTimeout = null,
        LlmRouterRetryOptions? retryOptions = null)
        : this(
            modelLookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            memory,
            maxPendingRequests,
            requestTimeout,
            retryOptions: retryOptions)
    {
        _routeProvider = routeProvider ??
            throw new ArgumentNullException(nameof(routeProvider));
    }

    /// <summary>
    /// Streams a completion for a model, using the endpoint the router would
    /// currently pick for that model. If that endpoint fails before producing
    /// meaningful output, the same request is retried against the model's next
    /// endpoint; the final event reports the attempt diagnostics.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="builder">Builds the request for the stream.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The canonical stream events.</returns>
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return StreamAsync(model, builder.Build(ModelStrategy.Auto), cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_gate is not null)
            await _gate.WaitAsync(cancellationToken);

        try
        {
            var candidates = await ResolveOrderedAsync(model, request, cancellationToken);

            await foreach (var evt in StreamThroughAsync(candidates, request, cancellationToken))
                yield return evt;
        }
        finally
        {
            _gate?.Release();
        }
    }

    /// <summary>
    /// Streams a completion for a strategy, using the endpoint the router
    /// would currently pick from the strategy's fallback chain. If that
    /// endpoint fails before producing meaningful output, the same request is
    /// retried against the chain's next endpoint; the final event reports the
    /// attempt diagnostics.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <param name="builder">Builds the request for the stream.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The canonical stream events.</returns>
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return StreamAsync(strategy, builder.Build(strategy), cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_gate is not null)
            await _gate.WaitAsync(cancellationToken);

        try
        {
            var candidates = await ResolveOrderedAsync(strategy, request, cancellationToken);

            await foreach (var evt in StreamThroughAsync(candidates, request, cancellationToken))
                yield return evt;
        }
        finally
        {
            _gate?.Release();
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<LlmStreamEvent> StreamRouteAsync(
        string route,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(builder);
        return StreamRouteAsync(
            route,
            builder.Build(ModelStrategy.Auto),
            cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> StreamRouteAsync(
        string route,
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(request);
        if (_gate is not null)
            await _gate.WaitAsync(cancellationToken);

        try
        {
            var candidates = await ResolveNamedRouteOrderedAsync(
                route,
                request,
                cancellationToken);

            await foreach (var evt in StreamThroughAsync(
                candidates,
                request,
                cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            _gate?.Release();
        }
    }

    /// <summary>
    /// The endpoint the router would currently use for a model, chosen from
    /// the model's configured endpoints by least-failing history.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The resolved endpoint.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the model has no registered endpoints.</exception>
    [Obsolete("Use ResolveAsync to avoid blocking asynchronous router memory.")]
    public ResolvedEndpoint Resolve(string model)
        => ResolveOrderedAsync(model, null, CancellationToken.None).GetAwaiter().GetResult().First();

    /// <inheritdoc />
    public async Task<ResolvedEndpoint> ResolveAsync(
        string model,
        CancellationToken cancellationToken = default) =>
        (await ResolveOrderedAsync(model, null, cancellationToken)).First();

    /// <summary>
    /// The endpoint the router would currently use for a strategy, chosen
    /// from the fallback chain's endpoints by least-failing history.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <returns>The resolved endpoint.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the strategy has no configured chain or none of its models
    /// are registered.
    /// </exception>
    [Obsolete("Use ResolveAsync to avoid blocking asynchronous router memory.")]
    public ResolvedEndpoint Resolve(ModelStrategy strategy)
        => ResolveOrderedAsync(strategy, null, CancellationToken.None).GetAwaiter().GetResult().First();

    /// <inheritdoc />
    public async Task<ResolvedEndpoint> ResolveAsync(
        ModelStrategy strategy,
        CancellationToken cancellationToken = default) =>
        (await ResolveOrderedAsync(strategy, null, cancellationToken)).First();

    /// <inheritdoc />
    public async Task<ResolvedEndpoint> ResolveRouteAsync(
        string route,
        CancellationToken cancellationToken = default) =>
        (await ResolveNamedRouteOrderedAsync(route, null, cancellationToken)).First();

    /// <inheritdoc />
    public async Task<LlmRouteExplanation> ExplainModelAsync(
        string model,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        (await ResolveRouteAsync(
            new LlmRoutingContext(LlmRouteTarget.Model(model), request),
            cancellationToken)).Explanation;

    /// <inheritdoc />
    public async Task<LlmRouteExplanation> ExplainStrategyAsync(
        ModelStrategy strategy,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        (await ResolveRouteAsync(
            new LlmRoutingContext(LlmRouteTarget.ForStrategy(strategy), request),
            cancellationToken)).Explanation;

    /// <inheritdoc />
    public async Task<LlmRouteExplanation> ExplainRouteAsync(
        string route,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        (await ResolveRouteAsync(
            new LlmRoutingContext(LlmRouteTarget.Named(route), request),
            cancellationToken)).Explanation;

    private async Task<IReadOnlyList<ResolvedEndpoint>> ResolveOrderedAsync(
        string model,
        LlmRequest? request,
        CancellationToken cancellationToken) =>
        (await ResolveRouteAsync(
            new LlmRoutingContext(LlmRouteTarget.Model(model), request),
            cancellationToken)).Endpoints;

    private async Task<IReadOnlyList<ResolvedEndpoint>> ResolveOrderedAsync(
        ModelStrategy strategy,
        LlmRequest? request,
        CancellationToken cancellationToken) =>
        (await ResolveRouteAsync(
            new LlmRoutingContext(LlmRouteTarget.ForStrategy(strategy), request),
            cancellationToken)).Endpoints;

    private async Task<IReadOnlyList<ResolvedEndpoint>> ResolveNamedRouteOrderedAsync(
        string route,
        LlmRequest? request,
        CancellationToken cancellationToken) =>
        (await ResolveRouteAsync(
            new LlmRoutingContext(LlmRouteTarget.Named(route), request),
            cancellationToken)).Endpoints;

    private async Task<LlmRouteResolution> ResolveRouteAsync(
        LlmRoutingContext context,
        CancellationToken cancellationToken)
    {
        var resolution = await RouteProvider.ResolveAsync(context, cancellationToken);
        if (resolution is null || resolution.Endpoints is null ||
            resolution.Explanation is null)
        {
            throw InvalidRouteProviderResult(
                context,
                "The route provider returned a null resolution, endpoint list, or explanation.");
        }

        if (resolution.Endpoints.Count == 0)
        {
            throw InvalidRouteProviderResult(
                context,
                "The route provider returned no execution candidates.",
                resolution);
        }

        var endpointIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in resolution.Endpoints)
        {
            if (!endpointIds.Add(endpoint.EndpointId))
            {
                throw InvalidRouteProviderResult(
                    context,
                    $"The route provider returned duplicate endpoint '{endpoint.EndpointId}'.",
                    resolution);
            }

            if (!modelLookup.TryGetClientByEndpointId(endpoint.EndpointId, out _))
            {
                throw InvalidRouteProviderResult(
                    context,
                    $"The route provider returned unknown endpoint '{endpoint.EndpointId}'.",
                    resolution);
            }
        }

        if (resolution.Explanation.Target != context.Target)
        {
            throw InvalidRouteProviderResult(
                context,
                "The route explanation target does not match the requested target.",
                resolution);
        }

        if (resolution.Explanation.SelectedEndpoint != resolution.Endpoints[0])
        {
            throw InvalidRouteProviderResult(
                context,
                "The route explanation's selected endpoint does not match the first execution candidate.",
                resolution);
        }

        return resolution;
    }

    private static LlmRoutingException InvalidRouteProviderResult(
        LlmRoutingContext context,
        string message,
        LlmRouteResolution? resolution = null) =>
        new(
            message,
            LlmRoutingFailureKind.InvalidProviderResult,
            context.Target,
            resolution?.Explanation?.ConfiguredModels,
            resolution?.Explanation?.Candidates);

    private async IAsyncEnumerable<LlmStreamEvent> StreamThroughAsync(
        IReadOnlyList<ResolvedEndpoint> candidates,
        LlmRequest wireRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timeoutCts = _requestTimeout is { } timeout
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCts?.CancelAfter(_requestTimeout!.Value);
        var effective = timeoutCts?.Token ?? cancellationToken;

        var attempts = new List<LlmRouterAttempt>();
        Exception? lastFailure = null;
        var incompatibleEndpoints = new HashSet<string>(StringComparer.Ordinal);

        for (var routeAttempt = 1;
             routeAttempt <= _retryOptions.MaximumAttempts;
             routeAttempt++)
        {
            var retryableFailure = false;
            TimeSpan? providerRetryAfter = null;

            foreach (var endpoint in candidates)
            {
                if (incompatibleEndpoints.Contains(endpoint.EndpointId))
                    continue;

                var client = modelLookup.GetClientByEndpointId(endpoint.EndpointId);
                var started = Stopwatch.GetTimestamp();
                using var attemptActivity = BaizeTelemetry.Activities.StartActivity(
                    "llm.router.attempt",
                    ActivityKind.Client);
                attemptActivity?.SetTag("gen_ai.request.model", endpoint.Model);
                attemptActivity?.SetTag("baize.endpoint.id", endpoint.EndpointId);
                attemptActivity?.SetTag("gen_ai.provider.name", endpoint.Provider.ToString());
                attemptActivity?.SetTag("gen_ai.operation.name", "chat");
                var telemetryTags = new TagList
            {
                { "gen_ai.operation.name", "chat" },
                { "gen_ai.provider.name", endpoint.Provider.ToString() },
                { "gen_ai.request.model", endpoint.Model },
                { "baize.endpoint.id", endpoint.EndpointId }
            };
                RouterTelemetry.Attempts.Add(1, telemetryTags);

                await _memory.RecordCallAsync(endpoint.EndpointId, cancellationToken);

                var emittedOutput = false;
                var shouldFallBack = false;
                List<LlmStreamEvent>? pending = null;

                await using var enumerator =
                    client.StreamAsync(wireRequest, effective)
                        .GetAsyncEnumerator(effective);

                while (true)
                {
                    LlmStreamEvent? evt = null;
                    Exception? failure = null;
                    Exception? incompatible = null;
                    DateTimeOffset? unavailableUntil = null;

                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                            break;

                        evt = enumerator.Current;
                    }
                    catch (Exception ex) when (
                        !cancellationToken.IsCancellationRequested &&
                        (IsAvailabilityFailure(ex) || ex is LlmRequestValidationException))
                    {
                        if (ex is LlmRequestValidationException)
                        {
                            incompatible = ex;
                        }
                        else
                        {
                            failure = ex;
                            unavailableUntil = (ex as LlmClientException)?.RateLimit?.UnavailableUntil;
                        }
                    }

                    if (incompatible is not null)
                    {
                        attemptActivity?.SetStatus(ActivityStatusCode.Error);
                        attemptActivity?.SetTag("error.type", incompatible.GetType().FullName);
                        // The request cannot be expressed on this endpoint's
                        // declared capabilities. Validation precedes any event, so
                        // nothing was emitted; the next capable candidate gets the
                        // request instead of the whole chain failing.
                        attempts.Add(new LlmRouterAttempt(
                            EndpointId: endpoint.EndpointId,
                            EndpointModel: endpoint.Model,
                            EndpointApiStyle: endpoint.Provider.ToString(),
                            Outcome: LlmRouterAttemptOutcome.Failed,
                            Duration: Stopwatch.GetElapsedTime(started),
                            Error: incompatible.Message));

                        lastFailure = incompatible;
                        incompatibleEndpoints.Add(endpoint.EndpointId);
                        RouterTelemetry.Failures.Add(1, telemetryTags);
                        RouterTelemetry.AttemptDuration.Record(
                            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                            telemetryTags);
                        shouldFallBack = true;
                        break;
                    }

                    if (failure is not null)
                    {
                        attemptActivity?.SetStatus(ActivityStatusCode.Error);
                        attemptActivity?.SetTag("error.type", failure.GetType().FullName);
                        await _memory.RecordFailureAsync(
                            endpoint.EndpointId,
                            LlmFailureCategory.Availability,
                            unavailableUntil,
                            cancellationToken);

                        attempts.Add(new LlmRouterAttempt(
                            EndpointId: endpoint.EndpointId,
                            EndpointModel: endpoint.Model,
                            EndpointApiStyle: endpoint.Provider.ToString(),
                            Outcome: LlmRouterAttemptOutcome.Failed,
                            Duration: Stopwatch.GetElapsedTime(started),
                            Error: failure.Message,
                            UnavailableUntil: unavailableUntil));

                        lastFailure = failure;
                        retryableFailure = true;
                        var retryAfter =
                            (failure as LlmClientException)?.RateLimit?.RetryAfter;
                        if (retryAfter is { } hint &&
                            (providerRetryAfter is null || hint > providerRetryAfter))
                        {
                            providerRetryAfter = hint;
                        }
                        RouterTelemetry.Failures.Add(1, telemetryTags);
                        RouterTelemetry.AttemptDuration.Record(
                            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                            telemetryTags);

                        // Never reissue after meaningful output has been streamed,
                        // and stop once the shared deadline has passed. Reasoning
                        // alone does not block reissue: it is held in the pending
                        // buffer and discarded on failover, so a failed endpoint's
                        // thoughts are never mixed with the next endpoint's answer.
                        // The same holds for events that precede any content
                        // (usage, diagnostics, finish reasons): they are buffered
                        // until a content or tool-call event commits the endpoint.
                        if (emittedOutput || effective.IsCancellationRequested)
                        {
                            yield return DiagnosticsEvent(attempts);
                            throw failure;
                        }

                        shouldFallBack = true;
                        break;
                    }

                    // A content or tool-call delta is the moment the endpoint
                    // commits: everything buffered before it (reasoning, usage,
                    // diagnostics, finish reasons) is released in order, then the
                    // committing event itself. Buffering until this point means a
                    // mid-stream transport failure exposes none of the endpoint's
                    // output, so failover cannot mix it with the next endpoint.
                    var commit = evt!.Delta is not null || evt.ToolCallDelta is not null;

                    if (!commit)
                    {
                        pending ??= [];
                        pending.Add(evt);
                        continue;
                    }

                    if (pending is not null)
                    {
                        foreach (var buffered in pending)
                            yield return buffered;

                        pending = null;
                    }

                    emittedOutput = true;

                    yield return evt;
                }

                if (shouldFallBack)
                {
                    RouterTelemetry.Fallbacks.Add(1, telemetryTags);
                    continue;
                }

                // The stream ended without a content or tool-call event (for
                // example a reasoning-only response followed by its usage and
                // finish reason): release the buffered events rather than dropping
                // them, preserving their order.
                if (pending is not null)
                {
                    foreach (var buffered in pending)
                        yield return buffered;
                }

                attempts.Add(new LlmRouterAttempt(
                    EndpointId: endpoint.EndpointId,
                    EndpointModel: endpoint.Model,
                    EndpointApiStyle: endpoint.Provider.ToString(),
                    Outcome: LlmRouterAttemptOutcome.Succeeded,
                    Duration: Stopwatch.GetElapsedTime(started)));
                attemptActivity?.SetStatus(ActivityStatusCode.Ok);
                RouterTelemetry.AttemptDuration.Record(
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    telemetryTags);

                yield return DiagnosticsEvent(attempts);
                yield break;
            }

            if (!retryableFailure ||
                routeAttempt >= _retryOptions.MaximumAttempts ||
                effective.IsCancellationRequested)
            {
                break;
            }

            var delay = _retryOptions.DelayForRetry(
                routeAttempt,
                providerRetryAfter);
            RouterTelemetry.Retries.Add(1);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, effective);
        }

        yield return DiagnosticsEvent(attempts);
        throw lastFailure ?? new LlmClientException("Every endpoint failed before producing output.");
    }

    private ILlmRouteProvider RouteProvider =>
        _routeProvider ??= new ConfiguredLlmRouteProvider(
            modelLookup,
            strategyLookup,
            _namedRouteLookup,
            _memory,
            _selectionPolicy);

    private static LlmStreamEvent DiagnosticsEvent(IReadOnlyList<LlmRouterAttempt> attempts) =>
        new(RouterDiagnostics: new LlmRouterDiagnostics(attempts));

    private static bool IsAvailabilityFailure(Exception ex) =>
        ex is HttpRequestException
            or TaskCanceledException
            or LlmClientException { CanFallback: true };

    private static LlmRouterRetryOptions ValidateRetryOptions(
        LlmRouterRetryOptions options)
    {
        options.Validate();
        return options;
    }
}
