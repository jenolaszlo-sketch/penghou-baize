using Penghou.Baize.Batch;
using FluentAssertions;

namespace Penghou.Baize.Batch.Tests;

public sealed class BatchClientResolverTests
{
    [Fact]
    public void GetClient_ResolvesRegisteredEndpoint()
    {
        var client = new StubBatchClient("OpenAi");
        var resolver = new BatchClientResolver(
            new Dictionary<string, IBaizeBatchClient>
            {
                ["gpt:OpenAi"] = client
            });

        resolver.GetClient("gpt:OpenAi").Should().BeSameAs(client);
    }

    [Fact]
    public void GetClient_UnknownEndpoint_Throws()
    {
        var resolver = new BatchClientResolver(
            new Dictionary<string, IBaizeBatchClient>());

        var action = () => resolver.GetClient("missing");

        action.Should().Throw<KeyNotFoundException>()
            .WithMessage("*'missing'*");
    }

    [Fact]
    public void TryGetClient_UnknownEndpoint_ReturnsFalse()
    {
        var resolver = new BatchClientResolver(
            new Dictionary<string, IBaizeBatchClient>());

        resolver.TryGetClient("missing", out _).Should().BeFalse();
    }

    [Fact]
    public void FactoryResolver_CreatesClientLazily()
    {
        var created = 0;
        var resolver = new BatchClientResolver(
            new Dictionary<string, Func<IBaizeBatchClient>>
            {
                ["gpt:OpenAi"] = () =>
                {
                    created++;
                    return new StubBatchClient("OpenAi");
                }
            });

        resolver.GetClient("gpt:OpenAi");
        resolver.GetClient("gpt:OpenAi");

        created.Should().Be(2);
    }

    private sealed class StubBatchClient(string providerId) : IBaizeBatchClient
    {
        public string ProviderId { get; } = providerId;
        public BatchCapabilities Capabilities { get; } = BatchCapabilities.NativeBatch;

        public Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderBatchHandle(ProviderId, "batch-id"));

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
