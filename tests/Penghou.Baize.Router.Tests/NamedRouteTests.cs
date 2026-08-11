using FluentAssertions;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Router.Tests;

public sealed class NamedRouteTests
{
    [Fact]
    public void TryValidate_RejectsUnknownModelInNamedRoute()
    {
        var options = new Configuration.LlmRoutingOptions
        {
            Models =
            [
                new Configuration.LlmModelOptions
                {
                    Name = "known",
                    Endpoints =
                    [
                        new Configuration.LlmEndpointOptions
                        {
                            ApiStyle = ApiStyle.Ollama
                        }
                    ]
                }
            ],
            NamedRoutes = new Dictionary<string, List<string>>
            {
                ["cheap"] = ["missing"]
            }
        };

        Extensions.ServiceCollectionExtensions.TryValidate(
            options,
            out var error).Should().BeFalse();
        error.Should().Contain("NamedRoutes['cheap']")
            .And.Contain("unknown model 'missing'");
    }

    [Fact]
    public async Task CompleteRouteAsync_UsesNamedFallbackChain()
    {
        var client = new EventClient("from cheap model");
        var lookup = Lookup("cheap-model", "cheap-endpoint", client);
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["low-cost"] = ["cheap-model"]
            });

        var response = await router.CompleteRouteAsync(
            "low-cost",
            new LlmRequest([new LlmMessage("user", "hello")]),
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from cheap model");
        var endpoint = await router.ResolveRouteAsync(
            "low-cost",
            TestContext.Current.CancellationToken);
        endpoint.EndpointId.Should().Be("cheap-endpoint");
    }

    [Fact]
    public async Task StreamRouteAsync_DoesNotInterpretRouteAsModelName()
    {
        var client = new EventClient("model response");
        var lookup = Lookup("low-cost", "model-endpoint", client);
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>());

        var action = async () => await router.CompleteRouteAsync(
            "low-cost",
            new LlmRequest([new LlmMessage("user", "hello")]),
            cancellationToken: TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<LlmRoutingException>()
            .WithMessage("*route 'low-cost'*");
        exception.Which.FailureKind.Should().Be(LlmRoutingFailureKind.RouteNotFound);
    }

    private static LlmModelLookup Lookup(
        string model,
        string endpointId,
        ILlmClient client)
    {
        var provider = new LlmProviderKey("Test");
        return new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                [model] = () => client
            },
            new Dictionary<(string Model, LlmProviderKey Provider), Func<ILlmClient>>
            {
                [(model, provider)] = () => client
            },
            byEndpointId: new Dictionary<string, Func<ILlmClient>>
            {
                [endpointId] = () => client
            },
            endpointsByModel:
                new Dictionary<string, IReadOnlyList<ResolvedEndpoint>>
                {
                    [model] = [new ResolvedEndpoint(endpointId, model, provider)]
                });
    }

    private sealed class EventClient(string content) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new LlmStreamEvent(Delta: content);
            yield return new LlmStreamEvent(FinishReason: "stop");
        }
    }
}
