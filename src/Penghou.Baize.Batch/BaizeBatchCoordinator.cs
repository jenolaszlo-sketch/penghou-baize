using System.Diagnostics;

namespace Penghou.Baize.Batch;

/// <summary>
/// Default aggregate batch coordinator with concurrent provider operations and
/// resilient completion polling.
/// </summary>
public sealed class BaizeBatchCoordinator(
    IBaizeBatchPlanner planner,
    IBaizeBatchClientResolver resolver,
    TimeProvider? timeProvider = null,
    Random? random = null) : IBaizeBatchCoordinator
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Random _random = random ?? Random.Shared;
    /// <inheritdoc />
    public async Task<BaizeBatchHandle> SubmitAsync(
        BaizeBatchSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var plan = planner.Plan(submission);
        using var activity = BaizeTelemetry.Activities.StartActivity(
            "llm.batch.submit",
            ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", "batch_submit");
        activity?.SetTag("baize.batch.id", plan.LogicalBatchId);
        activity?.SetTag("baize.batch.group_count", plan.Groups.Count);
        BatchTelemetry.Submissions.Add(
            1,
            new KeyValuePair<string, object?>("gen_ai.operation.name", "batch_submit"));
        var parts = new List<ProviderBatchPart>();

        for (var index = 0; index < plan.Groups.Count; index++)
        {
            var group = plan.Groups[index];
            var client = resolver.GetClient(group.EndpointId);

            try
            {
                var providerHandle = await client.SubmitAsync(
                    group.Items,
                    new BatchSubmissionOptions
                    {
                        IdempotencyKey = $"{plan.LogicalBatchId}:{index}",
                        Metadata = submission.Metadata
                    },
                    cancellationToken);
                parts.Add(new ProviderBatchPart(
                    providerHandle.ProviderId,
                    providerHandle.BatchId,
                    group.EndpointId,
                    group.Items.Select(item => item.RequestId).ToArray(),
                    providerHandle.Metadata));
            }
            catch (Exception exception) when
                (exception is not OperationCanceledException ||
                 !cancellationToken.IsCancellationRequested)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag("error.type", exception.GetType().FullName);
                var partial = new BaizeBatchHandle(
                    plan.LogicalBatchId,
                    parts.ToArray());
                throw new BaizeBatchSubmissionException(
                    $"Logical batch '{plan.LogicalBatchId}' failed while " +
                    $"submitting endpoint '{group.EndpointId}'. " +
                    $"{parts.Count} physical batch(es) were already accepted.",
                    partial,
                    exception);
            }
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        return new BaizeBatchHandle(plan.LogicalBatchId, parts.ToArray());
    }

    /// <inheritdoc />
    public async Task<BaizeBatchStatus> GetStatusAsync(
        BaizeBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        var statuses = await Task.WhenAll(handle.Parts.Select(async part =>
            new ProviderBatchPartStatus(
                part,
                await resolver.GetClient(part.EndpointId).GetStatusAsync(
                    ToProviderHandle(part),
                    cancellationToken))));

        return new BaizeBatchStatus(
            handle.LogicalBatchId,
            AggregateState(statuses.Select(value => value.Status.State)),
            handle.Parts.Sum(part => part.RequestIds.Count),
            statuses.Sum(value => value.Status.Completed ??
                (value.Status.State == BaizeBatchState.Completed
                    ? value.Part.RequestIds.Count
                    : 0)),
            statuses.Sum(value => value.Status.Failed ??
                (value.Status.State == BaizeBatchState.Failed
                    ? value.Part.RequestIds.Count
                    : 0)),
            statuses.ToArray());
    }

    /// <inheritdoc />
    public async Task<BaizeBatchStatus> WaitForCompletionAsync(
        BaizeBatchHandle handle,
        BatchWaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? new BatchWaitOptions();
        ValidateWaitOptions(effectiveOptions);
        var started = _timeProvider.GetUtcNow();
        var elapsedStarted = Stopwatch.GetTimestamp();
        using var activity = BaizeTelemetry.Activities.StartActivity(
            "llm.batch.wait",
            ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", "batch_wait");
        activity?.SetTag("baize.batch.id", handle.LogicalBatchId);
        var interval = effectiveOptions.PollInterval;
        var pollNumber = 0;
        var transientFailures = 0;

        try
        {
            while (true)
            {
                pollNumber++;
                BatchTelemetry.StatusPolls.Add(1);
                BaizeBatchStatus status;
                try
                {
                    status = await GetStatusAsync(handle, cancellationToken);
                    transientFailures = 0;
                }
                catch (Exception exception) when
                    (!cancellationToken.IsCancellationRequested &&
                     IsTransient(exception))
                {
                    transientFailures++;
                    BatchTelemetry.TransientFailures.Add(
                        1,
                        new KeyValuePair<string, object?>(
                            "error.type",
                            exception.GetType().Name));
                    if (transientFailures > effectiveOptions.MaxTransientFailures)
                        throw;

                    var retryAfter = (exception as LlmClientException)?
                        .RateLimit?.RetryAfter;
                    var retryDelay = ApplyJitter(
                        Max(interval, retryAfter),
                        effectiveOptions.JitterRatio);
                    retryDelay = LimitToRemaining(
                        handle,
                        retryDelay,
                        effectiveOptions.Timeout,
                        started);
                    effectiveOptions.Progress?.Report(new BatchPollingUpdate(
                        pollNumber,
                        Status: null,
                        transientFailures,
                        retryDelay,
                        exception.Message));
                    await Task.Delay(retryDelay, _timeProvider, cancellationToken);
                    interval = NextInterval(interval, effectiveOptions);
                    continue;
                }

                if (IsTerminal(status.State))
                {
                    effectiveOptions.Progress?.Report(new BatchPollingUpdate(
                        pollNumber,
                        status,
                        transientFailures));
                    activity?.SetTag("baize.batch.state", status.State.ToString());
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return status;
                }

                var providerDelay = status.Parts?
                    .Select(part => part.Status.RetryAfter)
                    .Where(value => value is not null)
                    .Max();
                var delay = ApplyJitter(
                    Max(interval, providerDelay),
                    effectiveOptions.JitterRatio);
                delay = LimitToRemaining(
                    handle,
                    delay,
                    effectiveOptions.Timeout,
                    started);
                effectiveOptions.Progress?.Report(new BatchPollingUpdate(
                    pollNumber,
                    status,
                    transientFailures,
                    delay));
                await Task.Delay(delay, _timeProvider, cancellationToken);
                interval = NextInterval(interval, effectiveOptions);
            }
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
        finally
        {
            BatchTelemetry.WaitDuration.Record(
                Stopwatch.GetElapsedTime(elapsedStarted).TotalMilliseconds);
        }
    }

    /// <inheritdoc />
    public async Task<BaizeBatchResultSet> GetResultsAsync(
        BaizeBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        var results = new List<BaizeBatchResult>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        var partResults = await Task.WhenAll(handle.Parts.Select(async part =>
            (Part: part, Results: await resolver.GetClient(part.EndpointId)
                .GetResultsAsync(ToProviderHandle(part), cancellationToken))));

        foreach (var (part, providerResults) in partResults)
        {
            foreach (var result in providerResults)
            {
                if (!part.RequestIds.Contains(result.RequestId, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Provider batch '{part.BatchId}' returned unexpected " +
                        $"request id '{result.RequestId}'.");
                }

                if (!ids.Add(result.RequestId))
                {
                    throw new InvalidOperationException(
                        $"Logical batch '{handle.LogicalBatchId}' returned duplicate " +
                        $"request id '{result.RequestId}'.");
                }

                results.Add(result);
            }
        }

        var expected = handle.Parts.SelectMany(part => part.RequestIds).ToHashSet(
            StringComparer.Ordinal);
        expected.ExceptWith(ids);
        if (expected.Count > 0)
        {
            throw new InvalidOperationException(
                $"Logical batch '{handle.LogicalBatchId}' omitted result(s): " +
                string.Join(", ", expected.OrderBy(id => id, StringComparer.Ordinal)));
        }

        return new BaizeBatchResultSet(
            handle.LogicalBatchId,
            AggregateResultState(results),
            results.ToArray());
    }

    /// <inheritdoc />
    public async Task<BaizeBatchResultSet> WaitForResultsAsync(
        BaizeBatchHandle handle,
        BatchWaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await WaitForCompletionAsync(handle, options, cancellationToken);
        return await GetResultsAsync(handle, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CancelAsync(
        BaizeBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);

        await Task.WhenAll(handle.Parts.Select(part =>
            resolver.GetClient(part.EndpointId).CancelAsync(
                ToProviderHandle(part),
                cancellationToken)));
    }

    private static ProviderBatchHandle ToProviderHandle(ProviderBatchPart part) =>
        new(
            part.ProviderId,
            part.BatchId,
            part.EndpointId,
            part.Metadata);

    private static void ValidateHandle(BaizeBatchHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle.LogicalBatchId);
        if (handle.Parts.Count == 0)
            throw new ArgumentException("Logical batch handle has no physical parts.", nameof(handle));
    }

    private static void ValidateWaitOptions(BatchWaitOptions options)
    {
        if (options.PollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "PollInterval must be positive.");
        if (options.MaxPollInterval < options.PollInterval)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxPollInterval cannot be shorter than PollInterval.");
        if (options.BackoffFactor < 1 || !double.IsFinite(options.BackoffFactor))
            throw new ArgumentOutOfRangeException(nameof(options), "BackoffFactor must be finite and at least 1.");
        if (options.JitterRatio is < 0 or > 1 || !double.IsFinite(options.JitterRatio))
            throw new ArgumentOutOfRangeException(nameof(options), "JitterRatio must be between 0 and 1.");
        if (options.MaxTransientFailures < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxTransientFailures cannot be negative.");
        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive when set.");
    }

    private TimeSpan ApplyJitter(TimeSpan delay, double jitterRatio)
    {
        if (jitterRatio == 0)
            return delay;

        var ratio = ((_random.NextDouble() * 2) - 1) * jitterRatio;
        return TimeSpan.FromTicks(Math.Max(
            1,
            (long)(delay.Ticks * (1 + ratio))));
    }

    private static TimeSpan NextInterval(
        TimeSpan current,
        BatchWaitOptions options)
    {
        var scaledTicks = current.Ticks * options.BackoffFactor;
        return scaledTicks >= options.MaxPollInterval.Ticks
            ? options.MaxPollInterval
            : TimeSpan.FromTicks((long)scaledTicks);
    }

    private TimeSpan LimitToRemaining(
        BaizeBatchHandle handle,
        TimeSpan delay,
        TimeSpan? timeout,
        DateTimeOffset started)
    {
        if (timeout is null)
            return delay;

        var remaining = timeout.Value - (_timeProvider.GetUtcNow() - started);
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException(
                $"Logical batch '{handle.LogicalBatchId}' did not complete " +
                $"within {timeout.Value}.");
        }

        return remaining < delay ? remaining : delay;
    }

    private static TimeSpan Max(TimeSpan interval, TimeSpan? recommendation) =>
        recommendation is { } value && value > interval ? value : interval;

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException ||
        exception is LlmClientException { CanFallback: true };

    private static bool IsTerminal(BaizeBatchState state) => state is
        BaizeBatchState.Completed or
        BaizeBatchState.PartiallyCompleted or
        BaizeBatchState.Failed or
        BaizeBatchState.Cancelled or
        BaizeBatchState.Expired;

    private static BaizeBatchState AggregateState(IEnumerable<BaizeBatchState> states)
    {
        var values = states.ToArray();
        if (values.All(state => state == BaizeBatchState.Completed))
            return BaizeBatchState.Completed;
        if (values.Any(state => state == BaizeBatchState.Running))
            return BaizeBatchState.Running;
        if (values.Any(state => state == BaizeBatchState.Cancelling))
            return BaizeBatchState.Cancelling;
        if (values.Any(state => state == BaizeBatchState.Pending))
        {
            return values.All(state => state == BaizeBatchState.Pending)
                ? BaizeBatchState.Pending
                : BaizeBatchState.Running;
        }
        if (values.All(state => state == BaizeBatchState.Failed))
            return BaizeBatchState.Failed;
        if (values.All(state => state == BaizeBatchState.Cancelled))
            return BaizeBatchState.Cancelled;
        if (values.All(state => state == BaizeBatchState.Expired))
            return BaizeBatchState.Expired;
        return BaizeBatchState.PartiallyCompleted;
    }

    private static BaizeBatchState AggregateResultState(
        IReadOnlyCollection<BaizeBatchResult> results)
    {
        if (results.All(result => result.State == BaizeBatchItemState.Succeeded))
            return BaizeBatchState.Completed;
        if (results.All(result => result.State == BaizeBatchItemState.Failed))
            return BaizeBatchState.Failed;
        if (results.All(result => result.State == BaizeBatchItemState.Cancelled))
            return BaizeBatchState.Cancelled;
        if (results.All(result => result.State == BaizeBatchItemState.Expired))
            return BaizeBatchState.Expired;
        return BaizeBatchState.PartiallyCompleted;
    }
}
