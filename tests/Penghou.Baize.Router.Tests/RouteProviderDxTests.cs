using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Router.Tests;

public sealed class RouteProviderDxTests
{
    [Fact]
    public async Task ExplainAsync_ReportsSelectionRankAndMemorySnapshot()
    {
        var memory = new InMemoryLlmRouterMemory();
        await memory.RecordCallAsync(
            "endpoint",
            TestContext.Current.CancellationToken);
        var endpoint = new ResolvedEndpoint(
            "endpoint",
            "model",
            new LlmProviderKey("test"));
        var lookup = Lookup(endpoint, new StubClient());
        var provider = new ConfiguredLlmRouteProvider(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            memory,
            new ReliabilityEndpointSelectionPolicy());

        var result = await provider.ResolveAsync(
            new LlmRoutingContext(LlmRouteTarget.Model("model")),
            TestContext.Current.CancellationToken);

        result.Explanation.SelectedEndpoint.Should().Be(endpoint);
        result.Explanation.Candidates.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new
            {
                Compatible = true,
                Rank = (int?)0
            });
        result.Explanation.Candidates[0].Stats.TotalCalls.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_ThrowsStructuredFailureForMissingNamedRoute()
    {
        var provider = new ConfiguredLlmRouteProvider(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>>(),
                new Dictionary<(string Model, LlmProviderKey Provider), Func<ILlmClient>>()),
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            new InMemoryLlmRouterMemory(),
            new ReliabilityEndpointSelectionPolicy());

        var action = async () => await provider.ResolveAsync(
            new LlmRoutingContext(LlmRouteTarget.Named("missing")),
            TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<LlmRoutingException>();
        exception.Which.FailureKind.Should().Be(LlmRoutingFailureKind.RouteNotFound);
        exception.Which.Target.Should().Be(LlmRouteTarget.Named("missing"));
    }

    [Fact]
    public async Task CustomRouteProvider_ReceivesRequestMetadata()
    {
        var endpoint = new ResolvedEndpoint(
            "endpoint",
            "model",
            new LlmProviderKey("test"));
        var lookup = Lookup(endpoint, new StubClient());
        var routeProvider = new CapturingRouteProvider(endpoint);
        var router = new LlmRouter(lookup, routeProvider);
        var request = new LlmRequest(
            [new LlmMessage("user", "hello")],
            metadata: new Dictionary<string, object?>
            {
                ["acme.tenant-id"] = "tenant-a"
            });

        await router.ExplainModelAsync(
            "model",
            request,
            TestContext.Current.CancellationToken);

        routeProvider.Request.Should().BeSameAs(request);
        routeProvider.Request!.Metadata["acme.tenant-id"].Should().Be("tenant-a");
    }

    [Theory]
    [InlineData("null-resolution", "null resolution")]
    [InlineData("null-endpoints", "null resolution")]
    [InlineData("null-explanation", "null resolution")]
    [InlineData("empty", "no execution candidates")]
    [InlineData("duplicate", "duplicate endpoint")]
    [InlineData("unknown", "unknown endpoint")]
    [InlineData("wrong-target", "target does not match")]
    [InlineData("wrong-selection", "selected endpoint does not match")]
    public async Task CustomRouteProvider_InvalidResultsFailBeforeExecution(
        string failureCase,
        string messageFragment)
    {
        var endpoint = new ResolvedEndpoint(
            "endpoint",
            "model",
            new LlmProviderKey("test"));
        var lookup = Lookup(endpoint, new StubClient());
        var router = new LlmRouter(
            lookup,
            new StaticRouteProvider(CreateInvalidResolution(failureCase, endpoint)));

        var action = () => router.ExplainModelAsync(
            "model",
            cancellationToken: TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<LlmRoutingException>();
        exception.Which.FailureKind.Should()
            .Be(LlmRoutingFailureKind.InvalidProviderResult);
        exception.Which.Target.Should().Be(LlmRouteTarget.Model("model"));
        exception.Which.Message.Should().Contain(messageFragment);
    }

    [Fact]
    public void FluentRegistration_ValidatesTheSameOptionGraph()
    {
        var services = new ServiceCollection();

        var action = () => services.AddLlmRouting(routes => routes
            .AddModel("known", model => model.AddEndpoint("test"))
            .AddNamedRoute("coding", "missing"));

        var exception = action.Should().Throw<LlmConfigurationException>().Which;
        exception.FailureKind.Should().Be(LlmConfigurationFailureKind.Structural);
        exception.Message.Should().Contain("unknown model 'missing'");
    }

    [Fact]
    public void FluentBuilder_ProducesValidOptionsWithCapabilities()
    {
        var builder = new LlmRoutingBuilder()
            .AddProfile("tools", capabilities => capabilities
                .SupportsTools(parallel: true)
                .SupportsStructuredOutput(viaTool: true)
                .SupportsThinking(
                    tokenBudget: 1024,
                    efforts: LlmThinkingEffort.Low)
                .StreamsToolCallArguments()
                .SupportsBatch(BatchCapabilities.NativeBatch)
                .SupportsContent(LlmContentType.Image, LlmContentTransport.InlineData))
            .AddModel("model", model => model.AddEndpoint("custom", endpoint => endpoint
                .WithId("primary")
                .UseProfile("tools")
                .UseProviderModel("provider-model")
                .UseBaseUrl("https://example.test")
                .UseSecret("API_KEY")
                .WithSetting("Dialect", "compatible")))
            .AddStrategy(ModelStrategy.Auto, "model")
            .AddNamedRoute("coding", "model")
            .WithMaxPendingRequests(3)
            .WithRequestTimeout(TimeSpan.FromSeconds(30));

        var options = builder.Build();
        ServiceCollectionExtensions.TryValidate(options, out var error).Should().BeTrue(error);
        options.Profiles["tools"].ParallelToolCalls.Should().BeTrue();
        options.Profiles["tools"].ThinkingBudget.Should().Be(1024);
        options.Profiles["tools"].Batch.Should().Be(BatchCapabilities.NativeBatch);
        options.Models[0].Endpoints[0].ApiKeySecretName.Should().Be("API_KEY");
        options.NamedRoutes["coding"].Should().Equal("model");
    }

    [Fact]
    public async Task StartupValidation_SucceedsWhenAllEndpointsInitialize()
    {
        var service = new LlmEndpointValidationHostedService(
            new StubValidator(new LlmEndpointValidationReport(
            [
                new LlmEndpointValidationResult(
                    "endpoint", "test", "model", true)
            ])));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartupValidation_ThrowsStructuredEndpointFailures()
    {
        var failed = new LlmEndpointValidationResult(
            "endpoint", "test", "model", false, "secret was not resolved");
        var service = new LlmEndpointValidationHostedService(
            new StubValidator(new LlmEndpointValidationReport([failed])));

        var action = () => service.StartAsync(TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<LlmConfigurationException>();
        exception.Which.FailureKind.Should()
            .Be(LlmConfigurationFailureKind.EndpointInitialization);
        exception.Which.EndpointFailures.Should().Equal(failed);
        exception.Which.Message.Should().Contain("endpoint")
            .And.Contain("secret was not resolved");
    }

    [Fact]
    public void RouteTargets_AreValidatedAndHaveUnambiguousNames()
    {
        LlmRouteTarget.Model("model").ToString().Should().Be("model:model");
        LlmRouteTarget.ForStrategy(ModelStrategy.Auto).ToString().Should()
            .Be("strategy:Auto");
        LlmRouteTarget.Named("coding").ToString().Should().Be("route:coding");
        var action = () => LlmRouteTarget.Named(" ");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StaticOptionsMonitor_AlwaysReturnsTheConfiguredValue()
    {
        var options = new LlmRoutingOptions();
        var monitor = new StaticOptionsMonitor<LlmRoutingOptions>(options);

        monitor.CurrentValue.Should().BeSameAs(options);
        monitor.Get("anything").Should().BeSameAs(options);
        monitor.OnChange((_, _) => { }).Should().BeNull();
    }

    private static LlmModelLookup Lookup(ResolvedEndpoint endpoint, ILlmClient client) =>
        new(
            new Dictionary<string, Func<ILlmClient>> { [endpoint.Model] = () => client },
            new Dictionary<(string Model, LlmProviderKey Provider), Func<ILlmClient>>
            {
                [(endpoint.Model, endpoint.Provider)] = () => client
            },
            byEndpointId: new Dictionary<string, Func<ILlmClient>>
            {
                [endpoint.EndpointId] = () => client
            },
            endpointsByModel: new Dictionary<string, IReadOnlyList<ResolvedEndpoint>>
            {
                [endpoint.Model] = [endpoint]
            });

    private static LlmRouteResolution CreateInvalidResolution(
        string failureCase,
        ResolvedEndpoint endpoint)
    {
        var unknown = new ResolvedEndpoint(
            "unknown",
            "model",
            new LlmProviderKey("test"));
        var target = failureCase == "wrong-target"
            ? LlmRouteTarget.Named("other")
            : LlmRouteTarget.Model("model");
        var endpoints = failureCase switch
        {
            "null-endpoints" => null!,
            "empty" => [],
            "duplicate" => [endpoint, endpoint],
            "unknown" => [unknown],
            _ => new[] { endpoint }
        };
        var selected = failureCase == "wrong-selection" ? unknown : endpoint;
        var stats = new LlmEndpointStats(endpoint.EndpointId, 0, 0, 0, 0);
        var explanation = failureCase == "null-explanation"
            ? null!
            : new LlmRouteExplanation(
                target,
                [endpoint.Model],
                [new LlmRouteCandidateExplanation(endpoint, true, null, 0, stats)],
                selected);
        return failureCase == "null-resolution"
            ? null!
            : new LlmRouteResolution(endpoints, explanation);
    }

    private sealed class StubClient : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class StubValidator(LlmEndpointValidationReport report)
        : ILlmEndpointValidator
    {
        public Task<LlmEndpointValidationReport> ValidateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(report);
    }

    private sealed class CapturingRouteProvider(ResolvedEndpoint endpoint)
        : ILlmRouteProvider
    {
        public LlmRequest? Request { get; private set; }

        public ValueTask<LlmRouteResolution> ResolveAsync(
            LlmRoutingContext context,
            CancellationToken cancellationToken = default)
        {
            Request = context.Request;
            var stats = new LlmEndpointStats(endpoint.EndpointId, 0, 0, 0, 0);
            var explanation = new LlmRouteExplanation(
                context.Target,
                [endpoint.Model],
                [new LlmRouteCandidateExplanation(endpoint, true, null, 0, stats)],
                endpoint);
            return ValueTask.FromResult(new LlmRouteResolution([endpoint], explanation));
        }
    }

    private sealed class StaticRouteProvider(LlmRouteResolution resolution)
        : ILlmRouteProvider
    {
        public ValueTask<LlmRouteResolution> ResolveAsync(
            LlmRoutingContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(resolution);
    }
}
