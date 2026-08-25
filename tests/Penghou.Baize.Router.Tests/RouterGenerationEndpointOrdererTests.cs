using FluentAssertions;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Router.Tests;

public sealed class RouterGenerationEndpointOrdererTests
{
    [Fact]
    public async Task OrderAsync_SkipsEndpointInSharedRouterCooldown()
    {
        var memory = new InMemoryLlmRouterMemory();
        await memory.RecordFailureAsync(
            "primary",
            LlmFailureCategory.Availability,
            DateTimeOffset.UtcNow.AddMinutes(1),
            TestContext.Current.CancellationToken);
        var orderer = new RouterGenerationEndpointOrderer(memory);
        GenerationEndpoint[] candidates =
        [
            new("provider", "primary", new StubClient()),
            new("provider", "backup", new StubClient())
        ];

        var ordered = await orderer.OrderAsync(
            candidates,
            TestContext.Current.CancellationToken);

        ordered.Select(endpoint => endpoint.EndpointId)
            .Should().Equal("backup");
    }

    private sealed class StubClient : IGenerationClient
    {
        public GenerationCapabilities Capabilities { get; } = new()
        {
            Features = GenerationFeature.None
        };

        public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerationOperation> GetAsync(GenerationOperationHandle handle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerationOperation> CancelAsync(GenerationOperationHandle handle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
