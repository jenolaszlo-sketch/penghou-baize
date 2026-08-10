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
public class LlmRouter(
    ILlmModelLookup modelLookup,
    IReadOnlyDictionary<ModelStrategy, IReadOnlyList<string>> strategyLookup,
    ILlmRouterMemory? memory = null,
    int maxPendingRequests = 0,
    TimeSpan? requestTimeout = null) : ILlmRouter
{
    private readonly ILlmRouterMemory _memory = memory ?? new InMemoryLlmRouterMemory();
    private readonly SemaphoreSlim? _gate =
        maxPendingRequests > 0 ? new SemaphoreSlim(maxPendingRequests) : null;
    private readonly TimeSpan? _requestTimeout = requestTimeout;

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
    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        ILlmPromptBuilder builder,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_gate is not null)
            await _gate.WaitAsync(cancellationToken);

        try
        {
            var candidates = await ResolveOrderedAsync(model, cancellationToken);

            await foreach (var evt in StreamThroughAsync(candidates, ModelStrategy.Auto, builder, cancellationToken))
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
    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        ILlmPromptBuilder builder,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_gate is not null)
            await _gate.WaitAsync(cancellationToken);

        try
        {
            var candidates = await ResolveOrderedAsync(strategy, cancellationToken);

            await foreach (var evt in StreamThroughAsync(candidates, strategy, builder, cancellationToken))
                yield return evt;
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
    public ResolvedEndpoint Resolve(string model)
        => ResolveOrderedAsync(model, CancellationToken.None).GetAwaiter().GetResult().First();

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
    public ResolvedEndpoint Resolve(ModelStrategy strategy)
        => ResolveOrderedAsync(strategy, CancellationToken.None).GetAwaiter().GetResult().First();

    private async Task<IReadOnlyList<ResolvedEndpoint>> ResolveOrderedAsync(
        string model,
        CancellationToken cancellationToken)
    {
        var candidates = ExpandCandidates([model]);

        if (candidates.Count == 0)
            throw new KeyNotFoundException($"No client registered for model '{model}'.");

        return await OrderCandidatesAsync(candidates, cancellationToken);
    }

    private async Task<IReadOnlyList<ResolvedEndpoint>> ResolveOrderedAsync(
        ModelStrategy strategy,
        CancellationToken cancellationToken)
    {
        if (!strategyLookup.TryGetValue(strategy, out var chain) || chain.Count == 0)
            throw new InvalidOperationException($"No models configured for strategy '{strategy}'.");

        var candidates = ExpandCandidates(chain);

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No model configured for strategy '{strategy}' is registered. " +
                $"Tried: {string.Join(", ", chain)}.");

        return await OrderCandidatesAsync(candidates, cancellationToken);
    }

    private async IAsyncEnumerable<LlmStreamEvent> StreamThroughAsync(
        IReadOnlyList<ResolvedEndpoint> candidates,
        ModelStrategy strategy,
        ILlmPromptBuilder builder,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timeoutCts = _requestTimeout is { } timeout
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCts?.CancelAfter(_requestTimeout!.Value);
        var effective = timeoutCts?.Token ?? cancellationToken;

        var wireRequest = builder.Build(strategy);
        var attempts = new List<LlmRouterAttempt>();
        Exception? lastFailure = null;

        foreach (var endpoint in candidates)
        {
            var client = modelLookup.GetClientByEndpointId(endpoint.EndpointId);
            var started = Stopwatch.GetTimestamp();

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
                    shouldFallBack = true;
                    break;
                }

                if (failure is not null)
                {
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
                continue;

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

            yield return DiagnosticsEvent(attempts);
            yield break;
        }

        yield return DiagnosticsEvent(attempts);
        throw lastFailure ?? new LlmClientException("Every endpoint failed before producing output.");
    }

    private List<ResolvedEndpoint> ExpandCandidates(IReadOnlyList<string> models)
    {
        var candidates = new List<ResolvedEndpoint>();

        foreach (var model in models)
            candidates.AddRange(modelLookup.GetEndpoints(model));

        return candidates;
    }

    private async Task<IReadOnlyList<ResolvedEndpoint>> OrderCandidatesAsync(
        IReadOnlyList<ResolvedEndpoint> candidates,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ranked = new List<(ResolvedEndpoint Endpoint, LlmEndpointStats Stats)>();

        foreach (var endpoint in candidates)
        {
            var stats = await _memory.GetStatsAsync(endpoint.EndpointId, cancellationToken);
            ranked.Add((endpoint, stats));
        }

        var available = ranked
            .Where(c => c.Stats.UnavailableUntil is null ||
                        c.Stats.UnavailableUntil <= now)
            .ToList();

        // If every candidate is cooled down, fall back to the least-failing
        // one rather than failing outright.
        var pool = available.Count > 0 ? available : ranked;

        // OrderBy/ThenBy are stable, so ties resolve to registration order.
        return pool
            .OrderBy(c => c.Stats.AvailabilityFailures)
            .ThenBy(c => QualityFailureRate(c.Stats))
            .Select(c => c.Endpoint)
            .ToList();
    }

    private static double QualityFailureRate(LlmEndpointStats stats) =>
        stats.TotalCalls == 0
            ? 0
            : (stats.ToolRepairFailures + stats.StructuredOutputFailures)
              / (double)stats.TotalCalls;

    private static LlmStreamEvent DiagnosticsEvent(IReadOnlyList<LlmRouterAttempt> attempts) =>
        new(RouterDiagnostics: new LlmRouterDiagnostics(attempts));

    private static bool IsAvailabilityFailure(Exception ex) =>
        ex is HttpRequestException
            or TaskCanceledException
            or LlmClientException { CanFallback: true };
}
