using FluentAssertions;
using Penghou.Baize;
using Penghou.Baize.Router;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Router.Tests;

public sealed class CapabilityRoutingTests
{
    [Fact]
    public async Task StreamAsync_FiltersEndpointsByMediaTransportBeforeSelection()
    {
        var plain = new TextClient("plain", new LlmEndpointCapabilities());
        var vision = new TextClient(
            "vision",
            new LlmEndpointCapabilities
            {
                ContentTypes = new HashSet<LlmContentType>
                {
                    LlmContentType.Text,
                    LlmContentType.Image
                },
                ContentTransports = new Dictionary<LlmContentType, LlmContentTransport>
                {
                    [LlmContentType.Image] = LlmContentTransport.InlineData
                }
            });
        var policy = new RecordingPolicy();
        var router = CreateRouter(plain, vision, policy);
        var builder = new LlmPromptBuilder
        {
            Messages =
            [
                new LlmMessage(
                    "user",
                    [new LlmImageContent("image/png", new LlmInlineDataSource(new byte[] { 1 }))])
            ]
        };

        var response = await router.CompleteStreamingAsync(
            "model",
            builder,
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("vision");
        policy.Candidates.Should().ContainSingle()
            .Which.EndpointId.Should().Be("vision");
        plain.CallCount.Should().Be(0);
        vision.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task StreamAsync_UsesInjectedSelectionPolicy()
    {
        var first = new TextClient("first", new LlmEndpointCapabilities());
        var second = new TextClient("second", new LlmEndpointCapabilities());
        var policy = new RecordingPolicy(reverse: true);
        var router = CreateRouter(first, second, policy);

        var response = await router.CompleteStreamingAsync(
            "model",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "hello")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("second");
        first.CallCount.Should().Be(0);
        second.CallCount.Should().Be(1);
    }

    private static LlmRouter CreateRouter(
        TextClient first,
        TextClient second,
        ILlmEndpointSelectionPolicy policy)
    {
        var firstProvider = new LlmProviderKey("first");
        var secondProvider = new LlmProviderKey("second");
        var endpoints = new[]
        {
            new ResolvedEndpoint("plain", "model", firstProvider),
            new ResolvedEndpoint("vision", "model", secondProvider)
        };
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>> { ["model"] = () => first },
            new Dictionary<(string, LlmProviderKey), Func<ILlmClient>>
            {
                [("model", firstProvider)] = () => first,
                [("model", secondProvider)] = () => second
            },
            byEndpointId: new Dictionary<string, Func<ILlmClient>>
            {
                ["plain"] = () => first,
                ["vision"] = () => second
            },
            endpointsByModel: new Dictionary<string, IReadOnlyList<ResolvedEndpoint>>
            {
                ["model"] = endpoints
            });

        return new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            selectionPolicy: policy);
    }

    private sealed class RecordingPolicy(bool reverse = false) : ILlmEndpointSelectionPolicy
    {
        public IReadOnlyList<ResolvedEndpoint> Candidates { get; private set; } = [];

        public Task<IReadOnlyList<ResolvedEndpoint>> OrderAsync(
            IReadOnlyList<ResolvedEndpoint> candidates,
            LlmRequest? request,
            ModelStrategy strategy,
            ILlmRouterMemory memory,
            CancellationToken cancellationToken = default)
        {
            Candidates = candidates.ToArray();
            return Task.FromResult<IReadOnlyList<ResolvedEndpoint>>(
                reverse ? candidates.Reverse().ToArray() : candidates.ToArray());
        }
    }

    private sealed class TextClient(
        string response,
        LlmEndpointCapabilities capabilities) : ILlmClient
    {
        public int CallCount { get; private set; }
        public LlmEndpointCapabilities Capabilities { get; } = capabilities;

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Yield();
            yield return new LlmStreamEvent(Delta: response);
            yield return new LlmStreamEvent(FinishReason: "stop");
        }
    }
}
