using Penghou.Baize;
using Penghou.Model.Abstractions;
using Penghou.Http.Abstractions;
using System.Runtime.CompilerServices;

var target = new ModelTarget("test", "model", "endpoint");
var invocation = new ModelInvocation("request", target, ModelOperation.Stream,
    new ModelExecutionContext("workflow", "activity", "agent", "correlation"), new ModelUsageIntent(1, 2));
var deny = new Deny<LlmRequest, LlmStreamEvent>();
try
{
    await foreach (var _ in deny.StreamAsync(new(invocation, new LlmRequest([new LlmMessage("user", "hello")])))) { }
    throw new InvalidOperationException("Denial was not observed.");
}
catch (ModelAccessDeniedException failure) when (failure.RequestId == invocation.RequestId) { }
var deniedClient = new DeniedClient();
try
{
    await foreach (var _ in deniedClient.StreamAsync(new LlmRequest([new LlmMessage("user", "hello")])
        { ExecutionContext = invocation.Context, UsageIntent = invocation.UsageIntent })) { }
    throw new InvalidOperationException("Baize semantic denial was not observed.");
}
catch (ModelAccessDeniedException) { }
if (deniedClient.RequestsCreated != 0) throw new InvalidOperationException("Denied Baize request reached provider shaping.");
var transport = new HttpFake();
using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1") { Content = new StringContent("body") };
using (var response = await BaizeHttpTransportAdapter.SendAsync(transport, request))
{
    if (await response.Content.ReadAsStringAsync() != "response") throw new InvalidOperationException("HTTP body changed.");
}
if (transport.Calls != 1 || transport.Disposals != 1) throw new InvalidOperationException("HTTP ownership changed.");
Console.WriteLine("Exact published model/HTTP contracts and packaged Baize adapter qualified.");

sealed class Deny<TRequest, TEvent> : IStreamingModelTransport<TRequest, TEvent> where TRequest : notnull where TEvent : notnull
{
    public async IAsyncEnumerable<ModelTransportResponse<TEvent>> StreamAsync(ModelTransportRequest<TRequest> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        throw new ModelAccessDeniedException(request.Invocation.RequestId, "denied");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
sealed class HttpFake : IHttpTransport
{
    public int Calls;
    public int Disposals;
    public ValueTask<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        return ValueTask.FromResult(new HttpTransportResponse(request.RequestId, 200, Penghou.Http.Abstractions.HttpHeaders.Empty, new Body(this)));
    }
    private sealed class Body(HttpFake owner) : IHttpResponseBody
    {
        private byte[] _bytes = "response"u8.ToArray();
        private int _offset;
        private bool _disposed;
        public ValueTask<HttpResponseChunk> ReadAsync(int maximumBytes, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(maximumBytes, _bytes.Length - _offset);
            var result = new HttpResponseChunk(_bytes.AsMemory(_offset, count), _offset + count == _bytes.Length);
            _offset += count;
            return ValueTask.FromResult(result);
        }
        public ValueTask DisposeAsync()
        {
            if (!_disposed) { _disposed = true; owner.Disposals++; }
            return ValueTask.CompletedTask;
        }
    }
}

sealed class DenyingFactory : IBaizeModelTransportFactory
{
    public IModelTransport<TRequest, TResponse> Wrap<TRequest, TResponse>(ModelTarget target,
        IModelTransport<TRequest, TResponse> trustedDefault) where TRequest : notnull where TResponse : notnull => new DenyingCall<TRequest, TResponse>();
    public IStreamingModelTransport<TRequest, TEvent> WrapStreaming<TRequest, TEvent>(ModelTarget target,
        IStreamingModelTransport<TRequest, TEvent> trustedDefault) where TRequest : notnull where TEvent : notnull => new Deny<TRequest, TEvent>();
    private sealed class DenyingCall<TRequest, TResponse> : IModelTransport<TRequest, TResponse> where TRequest : notnull where TResponse : notnull
    {
        public ValueTask<ModelTransportResponse<TResponse>> InvokeAsync(ModelTransportRequest<TRequest> request, CancellationToken cancellationToken = default) =>
            throw new ModelAccessDeniedException(request.Invocation.RequestId, "denied");
    }
}
sealed class DeniedClient : LlmClientBase
{
    public int RequestsCreated;
    public DeniedClient() : base("model", new NoNetworkFactory(), "", new LlmEndpointCapabilities(), "test")
    {
        ConfigureTransportEndpoint(new Uri("https://provider.example/stream"));
        ModelTransportFactory = new DenyingFactory();
    }
    protected override HttpRequestMessage CreateHttpRequest(LlmRequest request)
    {
        RequestsCreated++;
        throw new InvalidOperationException("Denied model call reached provider shaping.");
    }
    protected override IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(Stream stream, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Denied model call reached provider parsing.");
    private sealed class NoNetworkFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Unexpected provider connection.");
    }
}
