using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Router.Extensions;
using Penghou.Model.Abstractions;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Router.Tests;

public sealed class RoutedModelTransportTests
{
    [Fact]
    public async Task DenialIsLazyAndPrecedesSecretsAndProviderConstruction()
    {
        var fixture = new Fixture(_ => false);
        using var services = fixture.Build();
        var client = services.GetRequiredService<ILlmModelLookup>().GetClientByEndpointId("first");
        var stream = client.StreamAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Empty(fixture.Invocations);
        Assert.Equal(0, fixture.Secrets.Reads);
        await Assert.ThrowsAsync<ModelAccessDeniedException>(() => Collect(stream));
        Assert.Single(fixture.Invocations);
        Assert.Equal(0, fixture.Secrets.Reads);
        Assert.Equal(0, fixture.Provider.Constructions);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Fact]
    public async Task RevocationChecksEachCallWithoutReloadingSecretsOrCachingPermission()
    {
        var admitted = true;
        var fixture = new Fixture(_ => admitted);
        using var services = fixture.Build();
        var client = services.GetRequiredService<ILlmModelLookup>().GetClientByEndpointId("first");
        var request = Request();
        var events = await Collect(client.StreamAsync(request, TestContext.Current.CancellationToken));
        Assert.Contains(events, item => item.Delta == "result");
        admitted = false;
        await Assert.ThrowsAsync<ModelAccessDeniedException>(() => Collect(client.StreamAsync(request, TestContext.Current.CancellationToken)));
        Assert.Equal(2, fixture.Invocations.Count);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.Equal(1, fixture.Secrets.Reads);
        Assert.NotEqual(fixture.Invocations[0].RequestId, fixture.Invocations[1].RequestId);
        Assert.All(fixture.Invocations, invocation =>
        {
            Assert.Equal("first", invocation.Target.EndpointId);
            Assert.Equal("wire-model", invocation.Target.Model);
            Assert.Equal(request.ExecutionContext, invocation.Context);
            Assert.Equal(request.UsageIntent, invocation.UsageIntent);
        });
    }

    [Fact]
    public async Task ExplicitDenialStopsRouterFallback()
    {
        var fixture = new Fixture(_ => false);
        using var services = fixture.Build();
        var router = services.GetRequiredService<ILlmRouter>();
        await Assert.ThrowsAsync<ModelAccessDeniedException>(() => Collect(router.StreamAsync("logical-model", Request(), TestContext.Current.CancellationToken)));
        Assert.Single(fixture.Invocations);
        Assert.Equal(0, fixture.Provider.Constructions);
        Assert.Equal(0, fixture.Secrets.Reads);
    }

    [Fact]
    public async Task ProviderAvailabilityFallbackCrossesFreshSemanticBoundary()
    {
        var fixture = new Fixture(_ => true) { FailFirst = true };
        using var services = fixture.Build();
        var router = services.GetRequiredService<ILlmRouter>();
        var result = await Collect(router.StreamAsync("logical-model", Request(), TestContext.Current.CancellationToken));
        Assert.Contains(result, value => value.Delta == "result");
        Assert.Equal(new[] { "first", "second" }, fixture.Invocations.Select(value => value.Target.EndpointId));
        Assert.Equal(new[] { 1, 2 }, fixture.Invocations.Select(value => value.Attempt));
        Assert.Equal(2, fixture.Provider.Calls);
        Assert.Equal(2, fixture.Secrets.Reads);
    }

    [Fact]
    public async Task NativeCompletionChecksBeforeSecretResolution()
    {
        var fixture = new Fixture(_ => false);
        using var services = fixture.Build();
        var client = services.GetRequiredService<ILlmModelLookup>().GetClientByEndpointId("first");
        await Assert.ThrowsAsync<ModelAccessDeniedException>(() => client.CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(ModelOperation.Complete, Assert.Single(fixture.Invocations).Operation);
        Assert.Equal(0, fixture.Secrets.Reads);
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("payload")]
    public async Task SubstitutedRequestsFailBeforeCredentialResolution(string substitution)
    {
        var fixture = new Fixture(_ => true) { Substitution = substitution };
        using var services = fixture.Build();
        var client = services.GetRequiredService<ILlmModelLookup>().GetClientByEndpointId("first");
        if (substitution == "attempt")
            await Assert.ThrowsAsync<LlmClientException>(() => Collect(client.StreamAsync(Request(), TestContext.Current.CancellationToken)));
        else
            await Assert.ThrowsAsync<ArgumentException>(() => Collect(client.StreamAsync(Request(), TestContext.Current.CancellationToken)));
        Assert.Equal(0, fixture.Secrets.Reads);
        Assert.Equal(0, fixture.Provider.Constructions);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    private static LlmRequest Request() => new([new LlmMessage("user", "hello")])
    {
        ExecutionContext = new("workflow", "activity", "agent", "correlation", new Dictionary<string, string> { ["tenant"] = "example" }),
        UsageIntent = new(3, 10, 0.02m, "USD")
    };

    private static async Task<List<LlmStreamEvent>> Collect(IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var result = new List<LlmStreamEvent>();
        await foreach (var item in stream.WithCancellation(TestContext.Current.CancellationToken)) result.Add(item);
        return result;
    }

    private sealed class Fixture(Func<ModelInvocation, bool> decision)
    {
        public readonly List<ModelInvocation> Invocations = [];
        public readonly RecordingSecrets Secrets = new();
        public readonly RecordingProvider Provider = new();
        public bool FailFirst { get; init; }
        public string? Substitution { get; init; }

        public ServiceProvider Build()
        {
            Provider.FailFirst = FailFirst;
            var services = new ServiceCollection();
            services.AddHttpClient();
            services.AddSingleton<ISecretProvider>(Secrets);
            services.AddSingleton<ILlmClientProvider>(Provider);
            services.AddSingleton<IBaizeModelTransportFactory>(new CheckingFactory(invocation =>
            {
                Invocations.Add(invocation);
                return decision(invocation);
            }));
            if (Substitution is not null) services.AddSingleton<IBaizeModelTransportFactory>(new SubstitutingFactory(Substitution));
            services.AddLlmRouting(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LlmRouting:Models:0:Name"] = "logical-model",
                ["LlmRouting:Models:0:Endpoints:0:Provider"] = "test",
                ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] = "wire-model",
                ["LlmRouting:Models:0:Endpoints:0:Id"] = "first",
                ["LlmRouting:Models:0:Endpoints:0:ApiKeySecretName"] = "key-first",
                ["LlmRouting:Models:0:Endpoints:1:Provider"] = "test",
                ["LlmRouting:Models:0:Endpoints:1:ProviderModel"] = "wire-model",
                ["LlmRouting:Models:0:Endpoints:1:Id"] = "second",
                ["LlmRouting:Models:0:Endpoints:1:ApiKeySecretName"] = "key-second"
            }).Build());
            return services.BuildServiceProvider();
        }
    }

    private sealed class RecordingSecrets : ISecretProvider
    {
        public int Reads;
        public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<string?>(name);
        }
    }

    private sealed class RecordingProvider : ILlmClientProvider
    {
        public int Constructions;
        public int Calls;
        public bool FailFirst;
        public LlmProviderKey Key { get; } = new("test");
        public string DefaultBaseUrl => "https://provider.example/v1";
        public LlmEndpointCapabilities DefaultCapabilities { get; } = new();
        public ILlmClient CreateClient(LlmClientProviderContext context)
        {
            Constructions++;
            return new Client(this, context.ApiKey == "key-first");
        }

        private sealed class Client(RecordingProvider owner, bool first) : ILlmClient, ILlmCompletionClient
        {
            public LlmEndpointCapabilities Capabilities => owner.DefaultCapabilities;
            public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.Calls++;
                if (first && owner.FailFirst) throw new LlmClientException("Unavailable", 503);
                yield return new(Delta: "result");
                yield return new(FinishReason: "stop", Usage: new LlmUsage(3, 2, 5, 1, 2, 1),
                    Diagnostics: new LlmProviderDiagnostics("test", ResponseId: "provider-id"));
                await Task.CompletedTask;
            }
            public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
            {
                owner.Calls++;
                return Task.FromResult(new LlmResponse("result"));
            }
        }
    }

    private sealed class SubstitutingFactory(string substitution) : IBaizeModelTransportFactory
    {
        public IModelTransport<TRequest, TResponse> Wrap<TRequest, TResponse>(ModelTarget target,
            IModelTransport<TRequest, TResponse> inner) where TRequest : notnull where TResponse : notnull => inner;
        public IStreamingModelTransport<TRequest, TEvent> WrapStreaming<TRequest, TEvent>(ModelTarget target,
            IStreamingModelTransport<TRequest, TEvent> inner) where TRequest : notnull where TEvent : notnull => new Substitute<TRequest, TEvent>(inner, substitution);
    }
    private sealed class Substitute<TRequest, TEvent>(IStreamingModelTransport<TRequest, TEvent> inner, string substitution)
        : IStreamingModelTransport<TRequest, TEvent> where TRequest : notnull where TEvent : notnull
    {
        public IAsyncEnumerable<ModelTransportResponse<TEvent>> StreamAsync(ModelTransportRequest<TRequest> request, CancellationToken cancellationToken = default)
        {
            var current = request.Invocation;
            var invocation = new ModelInvocation(current.RequestId, current.Target, current.Operation, current.Context, current.UsageIntent,
                attempt: substitution == "attempt" ? current.Attempt + 1 : current.Attempt);
            var payload = request.Payload;
            if (substitution == "payload")
            {
                var chat = (LlmRequest)(object)payload;
                payload = (TRequest)(object)(new LlmRequest(chat.Messages, metadata: new Dictionary<string, object?> { ["callback"] = new Action(() => { }) }) { ExecutionContext = chat.ExecutionContext, UsageIntent = chat.UsageIntent });
            }
            return inner.StreamAsync(new(invocation, payload), cancellationToken);
        }
    }

    private sealed class CheckingFactory(Func<ModelInvocation, bool> admit) : IBaizeModelTransportFactory
    {
        public IModelTransport<TRequest, TResponse> Wrap<TRequest, TResponse>(ModelTarget target,
            IModelTransport<TRequest, TResponse> trustedDefault) where TRequest : notnull where TResponse : notnull => new Call<TRequest, TResponse>(admit, trustedDefault);
        public IStreamingModelTransport<TRequest, TEvent> WrapStreaming<TRequest, TEvent>(ModelTarget target,
            IStreamingModelTransport<TRequest, TEvent> trustedDefault) where TRequest : notnull where TEvent : notnull => new Streaming<TRequest, TEvent>(admit, trustedDefault);
    }

    private sealed class Call<TRequest, TResponse>(Func<ModelInvocation, bool> admit, IModelTransport<TRequest, TResponse> inner)
        : IModelTransport<TRequest, TResponse> where TRequest : notnull where TResponse : notnull
    {
        public ValueTask<ModelTransportResponse<TResponse>> InvokeAsync(ModelTransportRequest<TRequest> request, CancellationToken cancellationToken = default)
        {
            if (!admit(request.Invocation)) throw new ModelAccessDeniedException(request.Invocation.RequestId, "test_denied");
            return inner.InvokeAsync(request, cancellationToken);
        }
    }

    private sealed class Streaming<TRequest, TEvent>(Func<ModelInvocation, bool> admit, IStreamingModelTransport<TRequest, TEvent> inner)
        : IStreamingModelTransport<TRequest, TEvent> where TRequest : notnull where TEvent : notnull
    {
        public async IAsyncEnumerable<ModelTransportResponse<TEvent>> StreamAsync(ModelTransportRequest<TRequest> request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!admit(request.Invocation)) throw new ModelAccessDeniedException(request.Invocation.RequestId, "test_denied");
            await foreach (var response in inner.StreamAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return response;
        }
    }
}
