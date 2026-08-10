using FluentAssertions;

namespace Penghou.Baize.Router.Tests;

public sealed class EndpointBoundBatchClientTests
{
    [Fact]
    public async Task SubmitAsync_StampsConfiguredEndpointOnHandle()
    {
        var client = new EndpointBoundBatchClient("endpoint-1", new StubBatchClient());

        var handle = await client.SubmitAsync(
            [new BaizeBatchItem(
                "request-1",
                new LlmRequest([new LlmMessage("user", "hello")]))],
            cancellationToken: TestContext.Current.CancellationToken);

        handle.EndpointId.Should().Be("endpoint-1");
    }

    [Fact]
    public async Task GetStatusAsync_RejectsDifferentEndpoint()
    {
        var client = new EndpointBoundBatchClient("endpoint-1", new StubBatchClient());

        var action = async () => await client.GetStatusAsync(
            new ProviderBatchHandle("stub", "batch-1", "endpoint-2"),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*belongs to endpoint 'endpoint-2'*");
    }

    private sealed class StubBatchClient : IBaizeBatchClient
    {
        public string ProviderId => "stub";
        public BatchCapabilities Capabilities => BatchCapabilities.NativeBatch;

        public Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderBatchHandle(ProviderId, "batch-1"));

        public Task<ProviderBatchStatus> GetStatusAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderBatchStatus(BaizeBatchState.Pending));

        public Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaizeBatchResult>>([]);

        public Task CancelAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
