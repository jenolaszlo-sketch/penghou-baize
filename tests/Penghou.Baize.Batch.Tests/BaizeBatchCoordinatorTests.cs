using FluentAssertions;

namespace Penghou.Baize.Batch.Tests;

public sealed class BaizeBatchCoordinatorTests
{
    [Fact]
    public async Task SubmitAndResults_AggregatePhysicalBatches()
    {
        var request = new LlmRequest([new LlmMessage("user", "hello")]);
        var groups = new[]
        {
            new ProviderBatchGroup(
                "endpoint-a", "OpenAi", "model-a",
                [new BaizeBatchItem("one", request)]),
            new ProviderBatchGroup(
                "endpoint-b", "Claude", "model-b",
                [new BaizeBatchItem("two", request)])
        };
        var planner = new StubPlanner(new BatchPlan("logical", groups));
        var first = new StubBatchClient("OpenAi", "batch-a", "one");
        var second = new StubBatchClient("Claude", "batch-b", "two");
        var resolver = new BatchClientResolver(
            new Dictionary<string, IBaizeBatchClient>
            {
                ["endpoint-a"] = first,
                ["endpoint-b"] = second
            });
        var coordinator = new BaizeBatchCoordinator(planner, resolver);

        var handle = await coordinator.SubmitAsync(
            new BaizeBatchSubmission(
                [new BaizeBatchRequest("ignored", request, Model: "model-a")]),
            TestContext.Current.CancellationToken);
        var result = await coordinator.GetResultsAsync(
            handle,
            TestContext.Current.CancellationToken);

        handle.Parts.Should().HaveCount(2);
        first.LastOptions!.IdempotencyKey.Should().Be("logical:0");
        second.LastOptions!.IdempotencyKey.Should().Be("logical:1");
        result.State.Should().Be(BaizeBatchState.Completed);
        result.Results.Select(item => item.RequestId).Should().Equal("one", "two");
    }

    [Fact]
    public async Task SubmitFailure_PreservesAlreadyAcceptedHandles()
    {
        var request = new LlmRequest([new LlmMessage("user", "hello")]);
        var plan = new BatchPlan(
            "logical",
            [
                new ProviderBatchGroup(
                    "first", "OpenAi", null,
                    [new BaizeBatchItem("one", request)]),
                new ProviderBatchGroup(
                    "second", "Claude", null,
                    [new BaizeBatchItem("two", request)])
            ]);
        var resolver = new BatchClientResolver(
            new Dictionary<string, IBaizeBatchClient>
            {
                ["first"] = new StubBatchClient("OpenAi", "accepted", "one"),
                ["second"] = new StubBatchClient("Claude", failure: new HttpRequestException("down"))
            });
        var coordinator = new BaizeBatchCoordinator(new StubPlanner(plan), resolver);

        var action = () => coordinator.SubmitAsync(
            new BaizeBatchSubmission(
                [new BaizeBatchRequest("ignored", request)]),
            TestContext.Current.CancellationToken);

        var exception = await action.Should()
            .ThrowAsync<BaizeBatchSubmissionException>();
        exception.Which.PartialHandle.Parts.Should().ContainSingle();
        exception.Which.PartialHandle.Parts[0].BatchId.Should().Be("accepted");
    }

    [Fact]
    public async Task WaitForResults_PollsUntilTerminalThenFetchesResults()
    {
        var request = new LlmRequest([new LlmMessage("user", "hello")]);
        var plan = new BatchPlan(
            "logical",
            [
                new ProviderBatchGroup(
                    "endpoint", "OpenAi", "model",
                    [new BaizeBatchItem("one", request)])
            ]);
        var batchClient = new StubBatchClient(
            "OpenAi",
            requestId: "one",
            statuses: [BaizeBatchState.Pending, BaizeBatchState.Running,
                BaizeBatchState.Completed]);
        var coordinator = new BaizeBatchCoordinator(
            new StubPlanner(plan),
            new BatchClientResolver(
                new Dictionary<string, IBaizeBatchClient>
                {
                    ["endpoint"] = batchClient
                }));
        var handle = await coordinator.SubmitAsync(
            new BaizeBatchSubmission([new BaizeBatchRequest("one", request)]),
            TestContext.Current.CancellationToken);

        var progress = new List<BatchPollingUpdate>();
        var results = await coordinator.WaitForResultsAsync(
            handle,
            new BatchWaitOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(1),
                MaxPollInterval = TimeSpan.FromMilliseconds(2),
                BackoffFactor = 2,
                JitterRatio = 0,
                Timeout = TimeSpan.FromSeconds(2),
                Progress = new ImmediateProgress<BatchPollingUpdate>(progress.Add)
            },
            TestContext.Current.CancellationToken);

        batchClient.GetStatusCalls.Should().Be(3);
        results.Results.Should().ContainSingle()
            .Which.RequestId.Should().Be("one");
        progress.Select(update => update.NextDelay)
            .Should().Equal(
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(2),
                null);
    }

    [Fact]
    public async Task WaitForCompletion_RetriesTransientStatusFailure()
    {
        var request = new LlmRequest([new LlmMessage("user", "hello")]);
        var plan = new BatchPlan(
            "logical",
            [
                new ProviderBatchGroup(
                    "endpoint", "OpenAi", "model",
                    [new BaizeBatchItem("one", request)])
            ]);
        var client = new StubBatchClient(
            "OpenAi",
            requestId: "one",
            statusFailures:
            [
                new LlmClientException("busy", statusCode: 503)
            ]);
        var updates = new List<BatchPollingUpdate>();
        var coordinator = new BaizeBatchCoordinator(
            new StubPlanner(plan),
            new BatchClientResolver(
                new Dictionary<string, IBaizeBatchClient>
                {
                    ["endpoint"] = client
                }));
        var handle = await coordinator.SubmitAsync(
            new BaizeBatchSubmission([new BaizeBatchRequest("one", request)]),
            TestContext.Current.CancellationToken);

        var status = await coordinator.WaitForCompletionAsync(
            handle,
            new BatchWaitOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(1),
                JitterRatio = 0,
                Progress = new ImmediateProgress<BatchPollingUpdate>(updates.Add)
            },
            TestContext.Current.CancellationToken);

        status.State.Should().Be(BaizeBatchState.Completed);
        client.GetStatusCalls.Should().Be(2);
        updates[0].Error.Should().Be("busy");
        updates[0].ConsecutiveTransientFailures.Should().Be(1);
    }

    [Fact]
    public async Task GetStatus_QueriesPhysicalBatchesConcurrently()
    {
        var rendezvous = new StatusRendezvous(expected: 2);
        var first = new CoordinatedStatusClient("OpenAi", rendezvous);
        var second = new CoordinatedStatusClient("Claude", rendezvous);
        var coordinator = new BaizeBatchCoordinator(
            new StubPlanner(new BatchPlan("unused", [])),
            new BatchClientResolver(
                new Dictionary<string, IBaizeBatchClient>
                {
                    ["first"] = first,
                    ["second"] = second
                }));
        var handle = new BaizeBatchHandle(
            "logical",
            [
                new ProviderBatchPart("OpenAi", "a", "first", ["one"]),
                new ProviderBatchPart("Claude", "b", "second", ["two"])
            ]);

        var status = await coordinator.GetStatusAsync(
            handle,
            TestContext.Current.CancellationToken);

        status.State.Should().Be(BaizeBatchState.Completed);
        rendezvous.Started.Should().Be(2);
    }

    [Theory]
    [InlineData(BaizeBatchState.Running, BaizeBatchState.Failed, BaizeBatchState.Running)]
    [InlineData(BaizeBatchState.Cancelling, BaizeBatchState.Completed, BaizeBatchState.Cancelling)]
    [InlineData(BaizeBatchState.Pending, BaizeBatchState.Pending, BaizeBatchState.Pending)]
    [InlineData(BaizeBatchState.Pending, BaizeBatchState.Completed, BaizeBatchState.Running)]
    [InlineData(BaizeBatchState.Failed, BaizeBatchState.Failed, BaizeBatchState.Failed)]
    [InlineData(BaizeBatchState.Cancelled, BaizeBatchState.Cancelled, BaizeBatchState.Cancelled)]
    [InlineData(BaizeBatchState.Expired, BaizeBatchState.Expired, BaizeBatchState.Expired)]
    [InlineData(BaizeBatchState.Failed, BaizeBatchState.Completed, BaizeBatchState.PartiallyCompleted)]
    public async Task GetStatus_AggregatesProviderStates(
        BaizeBatchState firstState,
        BaizeBatchState secondState,
        BaizeBatchState expected)
    {
        var first = new StubBatchClient("OpenAi", statuses: [firstState]);
        var second = new StubBatchClient("Claude", statuses: [secondState]);
        var coordinator = new BaizeBatchCoordinator(
            new StubPlanner(new BatchPlan("unused", [])),
            new BatchClientResolver(
                new Dictionary<string, IBaizeBatchClient>
                {
                    ["first"] = first,
                    ["second"] = second
                }));
        var handle = new BaizeBatchHandle(
            "logical",
            [
                new ProviderBatchPart("OpenAi", "a", "first", ["one"]),
                new ProviderBatchPart("Claude", "b", "second", ["two"])
            ]);

        var status = await coordinator.GetStatusAsync(
            handle,
            TestContext.Current.CancellationToken);

        status.State.Should().Be(expected);
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("maximum")]
    [InlineData("backoff-small")]
    [InlineData("backoff-infinite")]
    [InlineData("jitter-small")]
    [InlineData("jitter-large")]
    [InlineData("failures")]
    [InlineData("timeout")]
    public async Task WaitForCompletion_RejectsInvalidPollingOptions(string scenario)
    {
        var options = scenario switch
        {
            "poll" => new BatchWaitOptions { PollInterval = TimeSpan.Zero },
            "maximum" => new BatchWaitOptions
            {
                PollInterval = TimeSpan.FromSeconds(2),
                MaxPollInterval = TimeSpan.FromSeconds(1)
            },
            "backoff-small" => new BatchWaitOptions { BackoffFactor = 0.5 },
            "backoff-infinite" => new BatchWaitOptions
            {
                BackoffFactor = double.PositiveInfinity
            },
            "jitter-small" => new BatchWaitOptions { JitterRatio = -0.1 },
            "jitter-large" => new BatchWaitOptions { JitterRatio = 1.1 },
            "failures" => new BatchWaitOptions { MaxTransientFailures = -1 },
            "timeout" => new BatchWaitOptions { Timeout = TimeSpan.Zero },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var client = new StubBatchClient("OpenAi");
        var coordinator = new BaizeBatchCoordinator(
            new StubPlanner(new BatchPlan("unused", [])),
            new BatchClientResolver(
                new Dictionary<string, IBaizeBatchClient>
                {
                    ["endpoint"] = client
                }));
        var handle = new BaizeBatchHandle(
            "logical",
            [new ProviderBatchPart("OpenAi", "batch", "endpoint", ["one"])]);

        var action = () => coordinator.WaitForCompletionAsync(
            handle,
            options,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
        client.GetStatusCalls.Should().Be(0);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveSubmissionConcurrency()
    {
        var action = () => new BaizeBatchCoordinator(
            new StubPlanner(new BatchPlan("unused", [])),
            new BatchClientResolver(new Dictionary<string, IBaizeBatchClient>()),
            new BatchCoordinatorOptions { MaxConcurrentSubmissions = 0 });

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*MaxConcurrentSubmissions*");
    }

    [Fact]
    public async Task CancelAsync_ForwardsEveryPhysicalHandle()
    {
        var first = new StubBatchClient("OpenAi");
        var second = new StubBatchClient("Claude");
        var coordinator = new BaizeBatchCoordinator(
            new StubPlanner(new BatchPlan("unused", [])),
            new BatchClientResolver(
                new Dictionary<string, IBaizeBatchClient>
                {
                    ["first"] = first,
                    ["second"] = second
                }));
        var handle = new BaizeBatchHandle(
            "logical",
            [
                new ProviderBatchPart("OpenAi", "a", "first", ["one"]),
                new ProviderBatchPart("Claude", "b", "second", ["two"])
            ]);

        await coordinator.CancelAsync(handle, TestContext.Current.CancellationToken);

        first.CancelCalls.Should().Be(1);
        second.CancelCalls.Should().Be(1);
    }

    private sealed class StubPlanner(BatchPlan plan) : IBaizeBatchPlanner
    {
        public BatchPlan Plan(BaizeBatchSubmission submission) => plan;
    }

    private sealed class StubBatchClient : IBaizeBatchClient
    {
        private readonly string _batchId;
        private readonly string? _requestId;
        private readonly Exception? _failure;
        private readonly Queue<BaizeBatchState> _statuses;
        private readonly Queue<Exception> _statusFailures;

        public StubBatchClient(
            string providerId,
            string batchId = "batch",
            string? requestId = null,
            Exception? failure = null,
            IEnumerable<BaizeBatchState>? statuses = null,
            IEnumerable<Exception>? statusFailures = null)
        {
            ProviderId = providerId;
            _batchId = batchId;
            _requestId = requestId;
            _failure = failure;
            _statuses = new Queue<BaizeBatchState>(
                statuses ?? [BaizeBatchState.Completed]);
            _statusFailures = new Queue<Exception>(statusFailures ?? []);
        }

        public string ProviderId { get; }
        public BatchCapabilities Capabilities =>
            BatchCapabilities.NativeBatch | BatchCapabilities.Cancellation;
        public BatchSubmissionOptions? LastOptions { get; private set; }
        public int GetStatusCalls { get; private set; }
        public int CancelCalls { get; private set; }

        public Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_failure is not null)
                return Task.FromException<ProviderBatchHandle>(_failure);
            LastOptions = options;
            return Task.FromResult(new ProviderBatchHandle(ProviderId, _batchId));
        }

        public Task<ProviderBatchStatus> GetStatusAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default)
        {
            GetStatusCalls++;
            if (_statusFailures.TryDequeue(out var failure))
                return Task.FromException<ProviderBatchStatus>(failure);

            var state = _statuses.Count > 1
                ? _statuses.Dequeue()
                : _statuses.Peek();
            return Task.FromResult(new ProviderBatchStatus(
                state,
                Completed: state == BaizeBatchState.Completed ? 1 : null));
        }

        public Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaizeBatchResult>>(
                [new BaizeBatchResult(
                    _requestId!,
                    BaizeBatchItemState.Succeeded,
                    new LlmResponse("ok"))]);

        public Task CancelAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default)
        {
            CancelCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class StatusRendezvous(int expected)
    {
        private readonly TaskCompletionSource _allStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _started) == expected)
                _allStarted.TrySetResult();
            await _allStarted.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CoordinatedStatusClient(
        string providerId,
        StatusRendezvous rendezvous) : IBaizeBatchClient
    {
        public string ProviderId { get; } = providerId;
        public BatchCapabilities Capabilities => BatchCapabilities.NativeBatch;

        public Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<ProviderBatchStatus> GetStatusAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default)
        {
            await rendezvous.ArriveAsync(cancellationToken);
            return new ProviderBatchStatus(BaizeBatchState.Completed);
        }

        public Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CancelAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
