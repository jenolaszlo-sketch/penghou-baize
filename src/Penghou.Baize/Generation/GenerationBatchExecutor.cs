using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Penghou.Baize.Generation;

/// <summary>
/// The default in-process <see cref="IGenerationBatchExecutor"/>. It routes the
/// base request once to learn the endpoint's native candidate limit, splits the
/// total count into chunks (reusing native multiple-candidate submissions where
/// supported), and is queue-aware for queued providers: every chunk is submitted
/// in a first pass (bounded by the batch concurrency) so the provider receives
/// the whole batch up front, then the pinned handles are polled in concurrent
/// waves until each reaches a terminal state. Synchronous providers that return
/// a terminal operation from submission skip the poll phase entirely.
/// Submission is at most once per chunk: ambiguous submission outcomes surface
/// as recorded failures and are never replayed; only status reads are retried.
/// Every per-chunk failure is recorded so callers get explicit partial results.
/// </summary>
public sealed class GenerationBatchExecutor : IGenerationBatchExecutor
{
    private readonly IGenerationClientRegistry _registry;
    private readonly IGenerationRoutingPolicy _routingPolicy;
    private readonly GenerationExecutorOptions _pollOptions;

    /// <summary>Initializes the batch executor.</summary>
    /// <param name="registry">The registry of registered generation endpoints.</param>
    /// <param name="routingPolicy">The routing policy, or the deterministic default when null.</param>
    /// <param name="options">The polling configuration used when waiting on queued handles, or defaults when null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
    public GenerationBatchExecutor(
        IGenerationClientRegistry registry,
        IGenerationRoutingPolicy? routingPolicy = null,
        IOptions<GenerationExecutorOptions>? options = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _routingPolicy = routingPolicy ?? new DefaultGenerationRoutingPolicy();
        _pollOptions = options?.Value ?? new GenerationExecutorOptions();
        ValidateOptions(_pollOptions);
    }

    /// <inheritdoc />
    public async Task<GenerationBatchResult> ExecuteAsync(
        GenerationBatchRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        if (request.TotalCount < 1)
            throw BaizeException.InvalidRequest(
                $"Generation batch TotalCount must be at least 1, but {request.TotalCount} was requested.");
        if (request.MaxConcurrency < 1)
            throw BaizeException.InvalidRequest(
                $"Generation batch MaxConcurrency must be at least 1, but {request.MaxConcurrency} was configured.");

        var endpoint = SelectEndpoint(request.Request);
        var chunkSize = ChunkSize(endpoint.Client.Capabilities, request.Request, request.TotalCount);
        var chunkCount = (int)Math.Ceiling(request.TotalCount / (double)chunkSize);

        var chunks = new GenerationBatchChunk[chunkCount];
        var pending = new List<PendingChunk>();
        var sync = new object();
        var progressGate = new BatchProgressGate(progress, chunkCount);
        var endpointDescription = Describe(endpoint);

        await SubmitAllChunksAsync(
            request, endpoint, chunkSize, chunkCount, chunks, pending, sync, progressGate, cancellationToken);

        if (pending.Count > 0)
        {
            await PollPendingHandlesAsync(
                request, endpoint, pending, sync, chunks, progressGate, endpointDescription, cancellationToken);
        }

        progress?.Report(1.0);
        return new GenerationBatchResult(chunks, request.TotalCount);
    }

    private async Task SubmitAllChunksAsync(
        GenerationBatchRequest request,
        GenerationEndpoint endpoint,
        int chunkSize,
        int chunkCount,
        GenerationBatchChunk[] chunks,
        List<PendingChunk> pending,
        object sync,
        BatchProgressGate progressGate,
        CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(
            Enumerable.Range(0, chunkCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = request.MaxConcurrency,
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                var count = Math.Min(chunkSize, request.TotalCount - (index * chunkSize));
                var slot = index * chunkSize;
                var chunkRequest = BuildChunkRequest(request.Request, count);
                try
                {
                    var operation = await endpoint.Client
                        .SubmitAsync(chunkRequest, token)
                        .ConfigureAwait(false);
                    var finalized = TryFinalize(slot, operation);
                    if (finalized is null)
                    {
                        lock (sync)
                        {
                            pending.Add(new PendingChunk(
                                index,
                                slot,
                                operation.Handle,
                                DateTimeOffset.UtcNow + _pollOptions.Timeout));
                        }
                    }
                    else
                    {
                        lock (sync)
                        {
                            chunks[index] = finalized;
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Caller-initiated cancellation propagates; submitted
                    // handles were already recorded by earlier iterations.
                    throw;
                }
                catch (BaizeException exception)
                {
                    lock (sync)
                    {
                        chunks[index] = new GenerationBatchChunk(slot, null, exception);
                    }
                }
                catch (Exception exception)
                {
                    // Never let an unexpected fault abort the submission
                    // sweep: chunks that already returned handles are billable
                    // and must be reported instead of discarded.
                    lock (sync)
                    {
                        chunks[index] = new GenerationBatchChunk(
                            slot,
                            null,
                            new BaizeException(
                                $"Chunk submission failed unexpectedly: {exception.Message}",
                                GenerationErrorKind.GenerationFailed,
                                innerException: exception));
                    }
                }
            }).ConfigureAwait(false);
    }

    private async Task PollPendingHandlesAsync(
        GenerationBatchRequest request,
        GenerationEndpoint endpoint,
        List<PendingChunk> pending,
        object sync,
        GenerationBatchChunk[] chunks,
        BatchProgressGate progressGate,
        string endpointDescription,
        CancellationToken cancellationToken)
    {
        var interval = _pollOptions.InitialPollingInterval;

        while (true)
        {
            PendingChunk[] wave;
            lock (sync)
            {
                wave = pending.ToArray();
            }
            if (wave.Length == 0)
                return;

            var completed = new ConcurrentDictionary<int, GenerationBatchChunk>();

            await Parallel.ForEachAsync(
                wave,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = request.MaxConcurrency,
                    CancellationToken = cancellationToken
                },
                async (item, token) =>
                {
                    if (DateTimeOffset.UtcNow >= item.Deadline)
                    {
                        completed[item.Chunk] = new GenerationBatchChunk(
                            item.Slot, null, CreateTimeout(item.Handle, endpointDescription));
                        return;
                    }

                    GenerationOperation snapshot;
                    try
                    {
                        snapshot = await endpoint.Client
                            .GetAsync(item.Handle, token)
                            .ConfigureAwait(false);
                    }
                    catch (BaizeException exception) when (
                        exception.ErrorKind is GenerationErrorKind.ProviderUnavailable
                            or GenerationErrorKind.RateLimited)
                    {
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (BaizeException exception)
                    {
                        completed[item.Chunk] = new GenerationBatchChunk(item.Slot, null, exception);
                        return;
                    }
                    catch (Exception exception)
                    {
                        // An unexpected polling fault must not abort the wave
                        // and strand the remaining billable handles.
                        completed[item.Chunk] = new GenerationBatchChunk(
                            item.Slot,
                            null,
                            new BaizeException(
                                $"Polling failed unexpectedly: {exception.Message}",
                                GenerationErrorKind.GenerationFailed,
                                innerException: exception));
                        return;
                    }

                    if (snapshot.Progress is { } value)
                        progressGate.Report(item.Chunk, Math.Clamp(value, 0.0, 1.0));

                    GenerationBatchChunk? finalized;
                    try
                    {
                        finalized = TryFinalize(item.Slot, snapshot);
                    }
                    catch (BaizeException exception)
                    {
                        finalized = new GenerationBatchChunk(item.Slot, null, exception);
                    }
                    if (finalized is not null)
                        completed[item.Chunk] = finalized;
                }).ConfigureAwait(false);

            if (!completed.IsEmpty)
            {
                lock (sync)
                {
                    foreach (var pair in completed)
                    {
                        chunks[pair.Key] = pair.Value;
                        pending.RemoveAll(item => item.Chunk == pair.Key);
                    }
                }
            }

            lock (sync)
            {
                if (pending.Count == 0)
                    return;
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            interval = TimeSpan.FromTicks(
                (long)Math.Min(
                    interval.Ticks * _pollOptions.PollingBackoffMultiplier,
                    _pollOptions.MaxPollingInterval.Ticks));
        }
    }

    private GenerationEndpoint SelectEndpoint(GenerationRequest request)
    {
        var candidates = _registry.Endpoints
            .Where(endpoint => Supports(endpoint, request))
            .ToArray();

        return _routingPolicy.Select(request, candidates)
            ?? throw new BaizeException(
                "No configured generation endpoint can satisfy the batch request.",
                GenerationErrorKind.InvalidRequest);
    }

    private static bool Supports(GenerationEndpoint endpoint, GenerationRequest request)
    {
        try
        {
            GenerationRequestValidator.Validate(
                endpoint.Client.Capabilities,
                request,
                Describe(endpoint));
            return true;
        }
        catch (BaizeException)
        {
            return false;
        }
    }

    private static string Describe(GenerationEndpoint endpoint) =>
        $"{endpoint.Provider}/{endpoint.EndpointId}";

    private static int ChunkSize(
        GenerationCapabilities capabilities,
        GenerationRequest request,
        int totalCount)
    {
        if (request is not ImageGenerationRequest)
            return 1;

        if (!capabilities.Supports(GenerationFeature.MultipleCandidates))
            return 1;

        var maximum = capabilities.MaximumCandidates ?? totalCount;
        return Math.Min(maximum, totalCount);
    }

    private static GenerationRequest BuildChunkRequest(GenerationRequest request, int count) =>
        request is ImageGenerationRequest image
            ? image with { Count = count }
            : request;

    private static GenerationBatchChunk? TryFinalize(int slot, GenerationOperation operation) =>
        operation.State switch
        {
            GenerationOperationState.Succeeded =>
                new GenerationBatchChunk(slot, RequireResult(operation), null),
            GenerationOperationState.Failed =>
                new GenerationBatchChunk(slot, null, CreateFailure(operation)),
            GenerationOperationState.Canceled =>
                new GenerationBatchChunk(slot, null, CreateCanceled(operation)),
            _ => null
        };

    private static GenerationResult RequireResult(GenerationOperation operation) =>
        operation.Result is { } result
            ? result
            : throw new BaizeException(
                $"Generation operation '{operation.Handle.Id}' succeeded but returned no assets.",
                GenerationErrorKind.GenerationFailed);

    private static BaizeException CreateFailure(GenerationOperation operation) =>
        operation.Error is { } error
            ? new BaizeException(
                error.Message ?? "Generation failed.",
                error.Kind,
                error.StatusCode,
                providerStatus: error.ProviderStatus)
            : new BaizeException(
                $"Generation operation '{operation.Handle.Id}' failed.",
                GenerationErrorKind.GenerationFailed);

    private static BaizeException CreateCanceled(GenerationOperation operation) =>
        new(
            $"Generation operation '{operation.Handle.Id}' was canceled.",
            GenerationErrorKind.Canceled);

    private static BaizeException CreateTimeout(
        GenerationOperationHandle handle,
        string endpointDescription) =>
        new(
            $"Generation operation '{handle.Id}' on endpoint '{endpointDescription}' did not " +
            "complete within the configured timeout. It may still be running; resume it later " +
            "with this handle.",
            GenerationErrorKind.TimeoutExceeded);

    private static void ValidateOptions(GenerationExecutorOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options), "Timeout must be positive.");
        if (options.InitialPollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options), "InitialPollingInterval must be positive.");
        if (options.MaxPollingInterval < options.InitialPollingInterval)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxPollingInterval must be at least InitialPollingInterval.");
        if (options.PollingBackoffMultiplier < 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(options), "PollingBackoffMultiplier must be at least 1.0.");
    }

    private sealed record PendingChunk(
        int Chunk,
        int Slot,
        GenerationOperationHandle Handle,
        DateTimeOffset Deadline);

    private sealed class BatchProgressGate
    {
        private readonly IProgress<double>? _outer;
        private readonly int _chunkCount;
        private readonly double[] _latest;
        private readonly object _gate = new();

        public BatchProgressGate(IProgress<double>? outer, int chunkCount)
        {
            _outer = outer;
            _chunkCount = chunkCount;
            _latest = new double[chunkCount];
        }

        public void Report(int chunk, double value)
        {
            lock (_gate)
            {
                _latest[chunk] = value;
                _outer?.Report(Math.Clamp(_latest.Average(), 0.0, 1.0));
            }
        }
    }
}