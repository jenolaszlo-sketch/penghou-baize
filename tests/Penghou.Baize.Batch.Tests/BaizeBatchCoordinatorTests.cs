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

    private sealed class StubPlanner(BatchPlan plan) : IBaizeBatchPlanner
    {
        public BatchPlan Plan(BaizeBatchSubmission submission) => plan;
    }

    private sealed class StubBatchClient : IBaizeBatchClient
    {
        private readonly string _batchId;
        private readonly string? _requestId;
        private readonly Exception? _failure;

        public StubBatchClient(
            string providerId,
            string batchId = "batch",
            string? requestId = null,
            Exception? failure = null)
        {
            ProviderId = providerId;
            _batchId = batchId;
            _requestId = requestId;
            _failure = failure;
        }

        public string ProviderId { get; }
        public BatchCapabilities Capabilities =>
            BatchCapabilities.NativeBatch | BatchCapabilities.Cancellation;
        public BatchSubmissionOptions? LastOptions { get; private set; }

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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderBatchStatus(BaizeBatchState.Completed, Completed: 1));

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
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
