namespace Penghou.Baize.Batch;

/// <summary>Default one-shot aggregate batch coordinator.</summary>
public sealed class BaizeBatchCoordinator(
    IBaizeBatchPlanner planner,
    IBaizeBatchClientResolver resolver,
    TimeProvider? timeProvider = null) : IBaizeBatchCoordinator
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    /// <inheritdoc />
    public async Task<BaizeBatchHandle> SubmitAsync(
        BaizeBatchSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var plan = planner.Plan(submission);
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

        return new BaizeBatchHandle(plan.LogicalBatchId, parts.ToArray());
    }

    /// <inheritdoc />
    public async Task<BaizeBatchStatus> GetStatusAsync(
        BaizeBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        var statuses = new List<ProviderBatchPartStatus>(handle.Parts.Count);

        foreach (var part in handle.Parts)
        {
            var status = await resolver.GetClient(part.EndpointId).GetStatusAsync(
                ToProviderHandle(part),
                cancellationToken);
            statuses.Add(new ProviderBatchPartStatus(part, status));
        }

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

        while (true)
        {
            var status = await GetStatusAsync(handle, cancellationToken);
            if (IsTerminal(status.State))
                return status;

            if (effectiveOptions.Timeout is { } timeout)
            {
                var remaining = timeout - (_timeProvider.GetUtcNow() - started);
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        $"Logical batch '{handle.LogicalBatchId}' did not complete " +
                        $"within {timeout}.");
                }

                await Task.Delay(
                    remaining < effectiveOptions.PollInterval
                        ? remaining
                        : effectiveOptions.PollInterval,
                    _timeProvider,
                    cancellationToken);
            }
            else
            {
                await Task.Delay(
                    effectiveOptions.PollInterval,
                    _timeProvider,
                    cancellationToken);
            }
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

        foreach (var part in handle.Parts)
        {
            var partResults = await resolver.GetClient(part.EndpointId).GetResultsAsync(
                ToProviderHandle(part),
                cancellationToken);

            foreach (var result in partResults)
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

        foreach (var part in handle.Parts)
        {
            await resolver.GetClient(part.EndpointId).CancelAsync(
                ToProviderHandle(part),
                cancellationToken);
        }
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
        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive when set.");
    }

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
