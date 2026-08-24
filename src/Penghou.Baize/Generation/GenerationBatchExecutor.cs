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
    private readonly GenerationExecutorCore _core;

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
        GenerationExecutorCore.ValidateOptions(_pollOptions);
        _core = new GenerationExecutorCore(_registry, _routingPolicy, _pollOptions);
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

    /// <inheritdoc />
    public Task<GenerationResult> WaitAsync(
        GenerationOperationHandle handle,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        _core.WaitAsync(handle, progress, cancellationToken);

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
                var chunkRequest = BuildChunkRequest(request.Request, index, count);
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

    private GenerationEndpoint SelectEndpoint(GenerationRequest request) =>
        _core.SelectEndpoint(request);

    private static bool Supports(GenerationEndpoint endpoint, GenerationRequest request) =>
        GenerationExecutorCore.Supports(endpoint, request);

    private static string Describe(GenerationEndpoint endpoint) =>
        GenerationExecutorCore.Describe(endpoint);

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

    private static GenerationRequest BuildChunkRequest(
        GenerationRequest request,
        int chunkIndex,
        int count)
    {
        // Each chunk carries a deterministic derived idempotency key so a
        // whole-batch replay cannot duplicate already-submitted billable
        // chunks on providers that honor keys.
        var keyed = request.IdempotencyKey is { } key
            ? request with { IdempotencyKey = $"{key}-{chunkIndex}" }
            : request;

        return keyed is ImageGenerationRequest image
            ? image with { Count = count }
            : keyed;
    }

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
        GenerationExecutorCore.RequireTerminalResult(operation);

    private static BaizeException CreateFailure(GenerationOperation operation) =>
        GenerationExecutorCore.CreateFailure(operation);

    private static BaizeException CreateCanceled(GenerationOperation operation) =>
        GenerationExecutorCore.CreateCanceled(operation);

    private static BaizeException CreateTimeout(
        GenerationOperationHandle handle,
        string endpointDescription) =>
        new(
            $"Generation operation '{handle.Id}' on endpoint '{endpointDescription}' did not " +
            "complete within the configured timeout. It may still be running; resume it later " +
            "by calling WaitAsync with this handle.",
            GenerationErrorKind.TimeoutExceeded);

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