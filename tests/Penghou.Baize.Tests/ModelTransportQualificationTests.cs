using Penghou.Model.Abstractions;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Penghou.Baize.Tests;

public sealed class ModelTransportQualificationTests
{
    [Fact]
    public async Task DefaultTransportExecutesFakeProviderWithoutAuthorityDependency()
    {
        var fixture = new Fixture();
        var events = await Collect(fixture.Client.StreamAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Contains(events, item => item.Delta == "provider");
        Assert.Equal(1, fixture.Handler.Calls);
    }

    [Fact]
    public async Task ReplacementDoesNotConnectToDefaultProviderAndPreservesActualUsage()
    {
        var fixture = new Fixture();
        fixture.Factory.Replace = true;
        fixture.Factory.Usage = new ModelUsage(inputTokens: 1, outputTokens: 2, cachedInputTokens: 3,
            thinkingTokens: 5, cost: .2m, currency: "USD", cacheMissInputTokens: 4);
        var events = await Collect(fixture.Client.StreamAsync(Request(), TestContext.Current.CancellationToken));
        var result = Assert.Single(events);
        Assert.Equal("replacement", result.Delta);
        Assert.Equal(0, fixture.Handler.Calls);
        Assert.Equal(0, fixture.Client.RequestsCreated);
        Assert.NotNull(result.Usage);
        Assert.Equal(3, result.Usage.PromptCacheHitTokens);
        Assert.Equal(4, result.Usage.PromptCacheMissTokens);
        Assert.Equal(5, result.Usage.ThinkingTokens);
        Assert.Null(result.Usage.TotalTokens);
        Assert.Equal(.2m, result.Usage.Cost);
        Assert.Equal("USD", result.Usage.Currency);
        Assert.Equal("provider-request", result.Diagnostics!.ResponseId);
    }

    [Fact]
    public async Task EachEnumerationChecksRevocationAndCarriesContextAndIntent()
    {
        var fixture = new Fixture();
        fixture.Factory.DenyAfterFirst = true;
        var request = Request();
        var stream = fixture.Client.StreamAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(0, fixture.Factory.Calls);
        await Collect(stream);
        await Assert.ThrowsAsync<ModelAccessDeniedException>(() => Collect(stream));
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Equal(2, fixture.Factory.Requests.Count);
        Assert.All(fixture.Factory.Requests, value =>
        {
            Assert.Equal(request.ExecutionContext, value.Invocation.Context);
            Assert.Equal(request.UsageIntent, value.Invocation.UsageIntent);
            Assert.Equal("test", value.Invocation.Target.Provider);
            Assert.Equal("model", value.Invocation.Target.Model);
            Assert.Equal(ModelOperation.Stream, value.Invocation.Operation);
        });
        Assert.NotEqual(fixture.Factory.Requests[0].Invocation.RequestId, fixture.Factory.Requests[1].Invocation.RequestId);
    }

    [Fact]
    public async Task MismatchedResponseIdentityIsRejectedBeforeEmission()
    {
        var fixture = new Fixture();
        fixture.Factory.Replace = true;
        fixture.Factory.WrongResponseId = true;
        var failure = await Assert.ThrowsAsync<LlmClientException>(() => Collect(fixture.Client.StreamAsync(Request(), TestContext.Current.CancellationToken)));
        Assert.Equal(LlmClientFailureKind.Protocol, failure.FailureKind);
        Assert.Equal(0, fixture.Handler.Calls);
    }

    [Fact]
    public async Task TargetSubstitutionCannotReachTheTrustedDefault()
    {
        var fixture = new Fixture();
        fixture.Factory.SubstituteTarget = true;
        await Assert.ThrowsAsync<LlmClientException>(() => Collect(fixture.Client.StreamAsync(Request(), TestContext.Current.CancellationToken)));
        Assert.Equal(0, fixture.Handler.Calls);
    }

    [Fact]
    public async Task SnapshotProtectsApprovedInlineBytesAndRejectsExecutableMetadata()
    {
        var fixture = new Fixture();
        fixture.Factory.Inspect = request =>
        {
            var image = Assert.IsType<LlmImageContent>(Assert.Single(Assert.Single(request.Payload.Messages).Parts));
            var source = Assert.IsType<LlmInlineDataSource>(image.Source);
            Assert.True(MemoryMarshal.TryGetArray(source.Data, out var detached));
            detached.Array![detached.Offset] = 9;
            Assert.Equal(1, source.Data.Span[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<LlmMessage>)request.Payload.Messages).Clear());
        };
        await Collect(fixture.Client.StreamAsync(new LlmRequest([new LlmMessage("user", [new LlmImageContent("image/png", new LlmInlineDataSource(new byte[] { 1, 2 }))])]), TestContext.Current.CancellationToken));
        var prior = fixture.Factory.Calls;
        var executable = new LlmRequest([new LlmMessage("user", "hello")], metadata: new Dictionary<string, object?> { ["callback"] = (Action)(() => { }) });
        await Assert.ThrowsAsync<ArgumentException>(() => Collect(fixture.Client.StreamAsync(executable, TestContext.Current.CancellationToken)));
        Assert.Equal(prior, fixture.Factory.Calls);
        Assert.Equal(1, fixture.Handler.Calls);
    }

    [Fact]
    public async Task CompletionUsesOneSemanticCompleteCheckAndPreservesProviderFailures()
    {
        var fixture = new Fixture();
        var response = await fixture.Client.CompleteAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal("provider", response.Content);
        Assert.Equal(ModelOperation.Complete, Assert.Single(fixture.Factory.Requests).Invocation.Operation);
        fixture.Handler.Status = HttpStatusCode.ServiceUnavailable;
        var failure = await Assert.ThrowsAsync<LlmClientException>(() => fixture.Client.CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.True(failure.CanFallback);
    }

    private static LlmRequest Request() => new([new LlmMessage("user", "hello")])
    {
        ExecutionContext = new("workflow", "activity", "agent", "correlation"),
        UsageIntent = new(4, 5, .1m, "USD")
    };
    private static async Task<List<LlmStreamEvent>> Collect(IAsyncEnumerable<LlmStreamEvent> values)
    {
        var result = new List<LlmStreamEvent>();
        await foreach (var value in values.WithCancellation(TestContext.Current.CancellationToken)) result.Add(value);
        return result;
    }
    private sealed class Fixture
    {
        public readonly Handler Handler = new();
        public readonly RecordingFactory Factory = new();
        public readonly Client Client;
        public Fixture() => Client = new Client(new HttpFactory(Handler)) { ModelTransportFactory = Factory };
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("provider") });
        }
    }
    private sealed class HttpFactory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Client(IHttpClientFactory factory) : LlmClientBase("model", factory, "", new(), "test")
    {
        public int RequestsCreated;
        protected override HttpRequestMessage CreateHttpRequest(LlmRequest request)
        {
            RequestsCreated++;
            return new HttpRequestMessage(HttpMethod.Post, "https://provider.test/stream") { Content = new StringContent("payload") };
        }
        protected override void ValidateRequest(LlmRequest request) { }
        protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream);
            yield return new(Delta: await reader.ReadToEndAsync(cancellationToken));
            yield return new(FinishReason: "stop");
        }
    }
    private sealed class RecordingFactory : IBaizeModelTransportFactory
    {
        public readonly List<ModelTransportRequest<LlmRequest>> Requests = [];
        public int Calls;
        public bool Replace;
        public bool WrongResponseId;
        public bool SubstituteTarget;
        public bool DenyAfterFirst;
        public ModelUsage? Usage;
        public Action<ModelTransportRequest<LlmRequest>>? Inspect;
        private void Check<T>(ModelTransportRequest<T> request) where T : notnull
        {
            Calls++;
            var typed = (ModelTransportRequest<LlmRequest>)(object)request;
            Requests.Add(typed);
            Inspect?.Invoke(typed);
            if (DenyAfterFirst && Calls > 1) throw new ModelAccessDeniedException(request.Invocation.RequestId, "revoked");
        }
        public IModelTransport<TRequest, TResponse> Wrap<TRequest, TResponse>(ModelTarget target,
            IModelTransport<TRequest, TResponse> trustedDefault) where TRequest : notnull where TResponse : notnull => new Call<TRequest, TResponse>(this, trustedDefault);
        public IStreamingModelTransport<TRequest, TEvent> WrapStreaming<TRequest, TEvent>(ModelTarget target,
            IStreamingModelTransport<TRequest, TEvent> trustedDefault) where TRequest : notnull where TEvent : notnull => new Streaming<TRequest, TEvent>(this, trustedDefault);
        private sealed class Call<TRequest, TResponse>(RecordingFactory owner, IModelTransport<TRequest, TResponse> inner)
            : IModelTransport<TRequest, TResponse> where TRequest : notnull where TResponse : notnull
        {
            public ValueTask<ModelTransportResponse<TResponse>> InvokeAsync(ModelTransportRequest<TRequest> request, CancellationToken cancellationToken = default)
            { owner.Check(request); return inner.InvokeAsync(request, cancellationToken); }
        }
        private sealed class Streaming<TRequest, TEvent>(RecordingFactory owner, IStreamingModelTransport<TRequest, TEvent> inner)
            : IStreamingModelTransport<TRequest, TEvent> where TRequest : notnull where TEvent : notnull
        {
            public async IAsyncEnumerable<ModelTransportResponse<TEvent>> StreamAsync(ModelTransportRequest<TRequest> request,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                owner.Check(request);
                if (owner.Replace)
                {
                    yield return new ModelTransportResponse<TEvent>(owner.WrongResponseId ? "wrong" : request.Invocation.RequestId,
                        (TEvent)(object)new LlmStreamEvent(Delta: "replacement", FinishReason: "stop"), owner.Usage, "provider-request");
                    yield break;
                }
                if (owner.SubstituteTarget)
                    request = new ModelTransportRequest<TRequest>(new ModelInvocation(request.Invocation.RequestId,
                        new ModelTarget("other", "model", "other"), request.Invocation.Operation,
                        request.Invocation.Context, request.Invocation.UsageIntent), request.Payload);
                await foreach (var value in inner.StreamAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return value;
            }
        }
    }
}
