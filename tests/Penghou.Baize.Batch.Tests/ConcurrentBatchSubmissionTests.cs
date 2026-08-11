using FluentAssertions;

namespace Penghou.Baize.Batch.Tests;

public sealed class ConcurrentBatchSubmissionTests
{
    private static readonly LlmRequest Request =
        new([new LlmMessage("user", "hello")]);

    [Fact]
    public async Task SubmitAsync_SubmitsConcurrentlyAndPreservesPlanOrder()
    {
        var rendezvous = new SubmissionRendezvous(2);
        var first = new CoordinatedClient("first-batch", rendezvous);
        var second = new CoordinatedClient("second-batch", rendezvous);
        var coordinator = Coordinator(
            [Group("first", "one"), Group("second", "two")],
            new Dictionary<string, IBaizeBatchClient>
            {
                ["first"] = first,
                ["second"] = second
            },
            maximumConcurrency: 2);

        var handle = await coordinator.SubmitAsync(
            Submission(),
            TestContext.Current.CancellationToken);

        rendezvous.PeakConcurrency.Should().Be(2);
        handle.Parts.Select(part => part.BatchId)
            .Should().Equal("first-batch", "second-batch");
    }

    [Fact]
    public async Task SubmitAsync_RespectsConfiguredConcurrencyLimit()
    {
        var rendezvous = new SubmissionRendezvous(2);
        var clients = new Dictionary<string, IBaizeBatchClient>
        {
            ["first"] = new CoordinatedClient("one", rendezvous),
            ["second"] = new CoordinatedClient("two", rendezvous),
            ["third"] = new CoordinatedClient("three", rendezvous)
        };
        var coordinator = Coordinator(
            [Group("first", "one"), Group("second", "two"), Group("third", "three")],
            clients,
            maximumConcurrency: 2);

        var handle = await coordinator.SubmitAsync(
            Submission(),
            TestContext.Current.CancellationToken);

        handle.Parts.Should().HaveCount(3);
        rendezvous.PeakConcurrency.Should().Be(2);
    }

    [Fact]
    public async Task SubmitAsync_AggregatesFailuresAndAllAcceptedHandles()
    {
        var coordinator = Coordinator(
            [Group("first", "one"), Group("second", "two"), Group("third", "three")],
            new Dictionary<string, IBaizeBatchClient>
            {
                ["first"] = new ImmediateClient("accepted"),
                ["second"] = new FailingClient(new HttpRequestException("down")),
                ["third"] = new FailingClient(new InvalidOperationException("invalid"))
            },
            maximumConcurrency: 3);

        var action = () => coordinator.SubmitAsync(
            Submission(),
            TestContext.Current.CancellationToken);

        var exception = await action.Should()
            .ThrowAsync<BaizeBatchSubmissionException>();
        exception.Which.PartialHandle.Parts.Should().ContainSingle()
            .Which.BatchId.Should().Be("accepted");
        exception.Which.Failures.Select(failure => failure.EndpointId)
            .Should().Equal("second", "third");
    }

    [Fact]
    public void Constructor_RejectsInvalidConcurrencyLimit()
    {
        var action = () => Coordinator(
            [],
            new Dictionary<string, IBaizeBatchClient>(),
            maximumConcurrency: 0);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*MaxConcurrentSubmissions*");
    }

    private static BaizeBatchCoordinator Coordinator(
        IReadOnlyList<ProviderBatchGroup> groups,
        IReadOnlyDictionary<string, IBaizeBatchClient> clients,
        int maximumConcurrency) =>
        new(
            new StubPlanner(new BatchPlan("logical", groups)),
            new BatchClientResolver(clients),
            new BatchCoordinatorOptions
            {
                MaxConcurrentSubmissions = maximumConcurrency
            });

    private static ProviderBatchGroup Group(string endpoint, string requestId) =>
        new(endpoint, "Test", endpoint, [new BaizeBatchItem(requestId, Request)]);

    private static BaizeBatchSubmission Submission() =>
        new([new BaizeBatchRequest("ignored", Request)]);

    private sealed class StubPlanner(BatchPlan plan) : IBaizeBatchPlanner
    {
        public BatchPlan Plan(BaizeBatchSubmission submission) => plan;
    }

    private sealed class SubmissionRendezvous(int expected)
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _arrived;
        private int _peak;

        public int PeakConcurrency => Volatile.Read(ref _peak);

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            UpdatePeak(active);
            if (Interlocked.Increment(ref _arrived) == expected)
                _release.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref _active);
        }

        private void UpdatePeak(int active)
        {
            var current = Volatile.Read(ref _peak);
            while (active > current)
            {
                var observed = Interlocked.CompareExchange(ref _peak, active, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }
    }

    private sealed class CoordinatedClient(
        string batchId,
        SubmissionRendezvous rendezvous) : BatchClient
    {
        public override async Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await rendezvous.ArriveAsync(cancellationToken);
            return new ProviderBatchHandle(ProviderId, batchId);
        }
    }

    private sealed class ImmediateClient(string batchId) : BatchClient
    {
        public override Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderBatchHandle(ProviderId, batchId));
    }

    private sealed class FailingClient(Exception error) : BatchClient
    {
        public override Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderBatchHandle>(error);
    }

    private abstract class BatchClient : IBaizeBatchClient
    {
        public string ProviderId => "Test";
        public BatchCapabilities Capabilities => BatchCapabilities.NativeBatch;

        public abstract Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default);

        public Task<ProviderBatchStatus> GetStatusAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
