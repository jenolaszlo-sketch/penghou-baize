using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Extensions;
using Penghou.Http.Abstractions;
using Penghou.Model.Abstractions;

// Deliberately small, non-durable fixtures. These demonstrate existing ports;
// they are not the BQ-1b admission system, budget ledger or transport backend.
sealed class Lease(string model)
{
    public static readonly AsyncLocal<Lease?> Current = new();
    public readonly string Model = model;
    public volatile bool Closed;
}

sealed class ModelProbe(int maximum = int.MaxValue) : IBaizeModelTransportFactory
{
    public int Calls;
    public string? ExpectedModel;
    public readonly List<Lease> Leases = [];
    public readonly List<ModelUsage?> Usage = [];
    private Lease Enter(ModelInvocation invocation)
    {
        if (Interlocked.Increment(ref Calls) > maximum || ExpectedModel is not null && ExpectedModel != invocation.Target.Model)
            throw new ModelAccessDeniedException(invocation.RequestId, "fixture-denied");
        var lease = new Lease(invocation.Target.Model);
        lock (Leases) Leases.Add(lease);
        return lease;
    }
    public IStreamingModelTransport<TRequest, TEvent> WrapStreaming<TRequest, TEvent>(ModelTarget target,
        IStreamingModelTransport<TRequest, TEvent> inner) where TRequest : notnull where TEvent : notnull => new Streaming<TRequest, TEvent>(this, inner);
    public IModelTransport<TRequest, TResponse> Wrap<TRequest, TResponse>(ModelTarget target,
        IModelTransport<TRequest, TResponse> inner) where TRequest : notnull where TResponse : notnull => new OneShot<TRequest, TResponse>(this, inner);

    private sealed class Streaming<TRequest, TEvent>(ModelProbe owner, IStreamingModelTransport<TRequest, TEvent> inner)
        : IStreamingModelTransport<TRequest, TEvent> where TRequest : notnull where TEvent : notnull
    {
        public async IAsyncEnumerable<ModelTransportResponse<TEvent>> StreamAsync(ModelTransportRequest<TRequest> request,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            var prior = Lease.Current.Value;
            var lease = owner.Enter(request.Invocation);
            Lease.Current.Value = lease;
            try
            {
                await foreach (var item in inner.StreamAsync(request, token).WithCancellation(token))
                {
                    lock (owner.Usage) owner.Usage.Add(item.Usage);
                    yield return item;
                }
            }
            finally { lease.Closed = true; Lease.Current.Value = prior; }
        }
    }
    private sealed class OneShot<TRequest, TResponse>(ModelProbe owner, IModelTransport<TRequest, TResponse> inner)
        : IModelTransport<TRequest, TResponse> where TRequest : notnull where TResponse : notnull
    {
        public async ValueTask<ModelTransportResponse<TResponse>> InvokeAsync(ModelTransportRequest<TRequest> request, CancellationToken token = default)
        {
            var prior = Lease.Current.Value;
            var lease = owner.Enter(request.Invocation);
            Lease.Current.Value = lease;
            try { return await inner.InvokeAsync(request, token); }
            finally { lease.Closed = true; Lease.Current.Value = prior; }
        }
    }
}

sealed class NeutralProbe(IHttpTransport inner) : IHttpTransport
{
    public int Calls;
    public int NullContexts;
    public int ActiveScopes;
    public Action? OnSend;
    public bool RequireActiveLease;
    public async ValueTask<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken token = default)
    {
        Interlocked.Increment(ref Calls);
        if (request.Context is null) Interlocked.Increment(ref NullContexts);
        if (Lease.Current.Value is { Closed: false }) Interlocked.Increment(ref ActiveScopes);
        if (RequireActiveLease && Lease.Current.Value is not { Closed: false }) throw new InvalidOperationException("fixture-closed-lease");
        OnSend?.Invoke();
        return await inner.SendAsync(request, token);
    }
}

sealed class Terminal(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
{
    public int Calls;
    public readonly List<string> Models = [];
    public readonly List<int?> OutputCaps = [];
    public int OtherHosts;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var call = Interlocked.Increment(ref Calls);
        if (request.RequestUri!.Host != "fixture.invalid") Interlocked.Increment(ref OtherHosts);
        if (request.Content is not null)
        {
            using var json = System.Text.Json.JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
            var root = json.RootElement;
            if (root.TryGetProperty("model", out var model)) lock (Models) Models.Add(model.GetString()!);
            int? cap = root.TryGetProperty("max_tokens", out var value) || root.TryGetProperty("max_completion_tokens", out value) ? value.GetInt32()
                : root.TryGetProperty("generationConfig", out var config) && config.TryGetProperty("maxOutputTokens", out value) ? value.GetInt32()
                : root.TryGetProperty("options", out var options) && options.TryGetProperty("num_predict", out value) ? value.GetInt32() : null;
            lock (OutputCaps) OutputCaps.Add(cap);
        }
        return await reply(request, call, token);
    }
}

sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    public void Dispose() => handler.Dispose();
}

sealed class RetryOnce(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var first = await base.SendAsync(request, token);
        if (first.StatusCode != HttpStatusCode.ServiceUnavailable) return first;
        first.Dispose();
        return await base.SendAsync(request, token);
    }
}

sealed class Retarget(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        request.RequestUri = new Uri("https://outside.invalid/v1/chat/completions");
        return base.SendAsync(request, token);
    }
}

sealed class LastGuard(HttpMessageHandler inner, int maximum = 1, bool bindHost = false) : DelegatingHandler(inner)
{
    public int Entries;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (Interlocked.Increment(ref Entries) > maximum || bindHost && request.RequestUri!.Host != "fixture.invalid")
            throw new HttpRequestException("fixture-final-boundary-denied");
        return base.SendAsync(request, token);
    }
}

sealed class Secrets(bool delayed = false) : ISecretProvider
{
    public int Reads;
    public bool TokenCanBeCancelled;
    public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource<string?> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<string?> GetSecretAsync(string name, CancellationToken token = default)
    {
        Interlocked.Increment(ref Reads);
        TokenCanBeCancelled = token.CanBeCanceled;
        Started.TrySetResult();
        return delayed ? await Release.Task : "FAKE";
    }
}

sealed class ProbeProvider(ModelProbe? innerProbe = null) : ILlmClientProvider
{
    public int Constructions;
    public readonly TaskCompletionSource Created = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public LlmProviderKey Key { get; } = new("probe");
    public string DefaultBaseUrl => "https://fixture.invalid/v1";
    public LlmEndpointCapabilities DefaultCapabilities { get; } = new();
    public ILlmClient CreateClient(LlmClientProviderContext context)
    {
        Interlocked.Increment(ref Constructions);
        var client = (LlmClientBase)new OpenAiClientProvider().CreateClient(context);
        if (innerProbe is not null) client.ModelTransportFactory = innerProbe;
        Created.TrySetResult();
        return client;
    }
}

sealed class RouteFixture : IAsyncDisposable
{
    public readonly ModelProbe Model;
    public readonly Secrets Secrets;
    public readonly NeutralProbe Http;
    public readonly ServiceProvider Services;
    public readonly IConfigurationRoot Config;
    public ILlmRouter Router => Services.GetRequiredService<ILlmRouter>();
    public RouteFixture(Factory factory, ModelProbe? model = null, Secrets? secrets = null, ProbeProvider? provider = null, int endpoints = 1)
    {
        Model = model ?? new(); Secrets = secrets ?? new();
        Http = new(new BaizeHttpTransport(factory));
        var values = new Dictionary<string, string?> { ["LlmRouting:Retry:MaximumAttempts"] = "1" };
        values["LlmRouting:Models:0:Name"] = "fixture";
        for (var i = 0; i < endpoints; i++)
        {
            var prefix = $"LlmRouting:Models:0:Endpoints:{i}:";
            values[prefix + "Id"] = $"endpoint-{i}";
            values[prefix + "Provider"] = provider is null ? "OpenAi" : "probe";
            values[prefix + "ProviderModel"] = "fixture-model";
            values[prefix + "BaseUrl"] = $"https://fixture.invalid/{i}/v1";
            values[prefix + "ApiKeySecretName"] = "fixture-key";
        }
        Config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddSingleton<IHttpTransport>(Http);
        services.AddSingleton<IBaizeModelTransportFactory>(Model);
        services.AddSingleton<ISecretProvider>(Secrets);
        services.AddSingleton<ILlmClientProvider>(provider is null ? new OpenAiClientProvider() : provider);
        services.AddLlmRouting(Config);
        Services = services.BuildServiceProvider();
    }
    public ValueTask DisposeAsync() { (Config as IDisposable)?.Dispose(); return Services.DisposeAsync(); }
}

sealed class ControlledStream(byte[]? bytes = null, bool blockReads = false, bool blockDispose = false) : Stream
{
    public readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource DisposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource ReleaseDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int ReadBytes;
    public bool Disposed;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        ReadStarted.TrySetResult();
        if (blockReads) await Task.Delay(Timeout.InfiniteTimeSpan, token);
        var count = Math.Min(buffer.Length, (bytes?.Length ?? 0) - ReadBytes);
        bytes?.AsMemory(ReadBytes, count).CopyTo(buffer);
        ReadBytes += count;
        return count;
    }
    protected override void Dispose(bool disposing)
    {
        DisposeStarted.TrySetResult();
        if (blockDispose) ReleaseDispose.Task.GetAwaiter().GetResult();
        Disposed = true;
        base.Dispose(disposing);
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
