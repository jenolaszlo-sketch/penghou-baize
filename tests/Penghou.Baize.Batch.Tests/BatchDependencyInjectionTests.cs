using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Router;

namespace Penghou.Baize.Batch.Tests;

public sealed class BatchDependencyInjectionTests
{
    [Fact]
    public void AddBaizeBatch_ResolvesPlannerAndRouterBackedClient()
    {
        var batchClient = new StubBatchClient();
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>(),
            new Dictionary<(string, LlmProviderKey), Func<ILlmClient>>(),
            byEndpointId: new Dictionary<string, Func<ILlmClient>>(),
            batchByEndpointId: new Dictionary<string, Func<IBaizeBatchClient>>
            {
                ["endpoint-1"] = () => batchClient
            });
        var services = new ServiceCollection();
        services.AddSingleton<ILlmModelLookup>(lookup);
        services.AddBaizeBatch();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBaizeBatchPlanner>().Should().NotBeNull();
        provider.GetRequiredService<IBaizeBatchClientResolver>()
            .GetClient("endpoint-1").Should().BeSameAs(batchClient);
    }

    [Fact]
    public void AddBaizeBatch_RejectsInvalidGroupingLimit()
    {
        var services = new ServiceCollection();

        var action = () => services.AddBaizeBatch(
            new BatchPlannerOptions { MaxItemsPerGroup = 0 });

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class StubBatchClient : IBaizeBatchClient
    {
        public string ProviderId => "stub";
        public BatchCapabilities Capabilities => BatchCapabilities.NativeBatch;

        public Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
