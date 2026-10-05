using System.Net;
using System.Text;
using Penghou.Http.Abstractions;
using Penghou.Baize;
using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class BaizeHttpTransportAdoptionTests
{
    [Fact]
    public async Task Adapter_SnapshotsRequestAndOwnsResponseBody()
    {
        var handler = new RecordingHandler();
        var transport = new BaizeHttpTransport(new SingleClientFactory(new HttpClient(handler)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/items")
        { Content = new StringContent("payload", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Trace", "kept");
        using var response = await BaizeHttpTransportAdapter.SendAsync(transport, request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("reply");
        (await handler.Request!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("payload");
        handler.Request.Headers.GetValues("X-Trace").Should().ContainSingle().Which.Should().Be("kept");
        handler.ResponseDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Adapter_RejectsUnboundedLegacyBodyBeforeTransportDispatch()
    {
        var transport = new RecordingTransport();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/") { Content = new StringContent("12345") };
        var act = () => BaizeHttpTransportAdapter.SendAsync(transport, request, new HttpTransportLimits(maximumRequestBodyBytes: 4), null, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidDataException>();
        transport.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DefaultTransport_RejectsRedirectRequestsAndOversizedResponse()
    {
        var handler = new RecordingHandler { ResponseBytes = "12345"u8.ToArray() };
        var transport = new BaizeHttpTransport(new SingleClientFactory(new HttpClient(handler)));
        var redirect = new HttpTransportRequest("r1", "GET", new Uri("https://example.test/"), limits: new HttpTransportLimits(maximumRedirects: 1));
        var unsupported = () => transport.SendAsync(redirect, TestContext.Current.CancellationToken).AsTask();
        await unsupported.Should().ThrowAsync<NotSupportedException>();
        handler.Calls.Should().Be(0);

        var request = new HttpTransportRequest("r2", "GET", new Uri("https://example.test/"), limits: new HttpTransportLimits(maximumResponseBodyBytes: 4, maximumChunkBytes: 4));
        var response = await transport.SendAsync(request, TestContext.Current.CancellationToken);
        await response.Body.ReadAsync(4, TestContext.Current.CancellationToken);
        var read = () => response.Body.ReadAsync(4, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        handler.ResponseDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Adapter_PreservesBytesFromPartialTerminalChunk()
    {
        var transport = new FixedResponseTransport(id => new HttpTransportResponse(id, 200, Penghou.Http.Abstractions.HttpHeaders.Empty, new FixedBody("done"u8.ToArray())));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await BaizeHttpTransportAdapter.SendAsync(transport, request, TestContext.Current.CancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        var bytes = new byte[8];
        var total = await stream.ReadAsync(bytes.AsMemory(0, 2), TestContext.Current.CancellationToken);
        total += await stream.ReadAsync(bytes.AsMemory(2, 2), TestContext.Current.CancellationToken);
        total += await stream.ReadAsync(bytes.AsMemory(4, 2), TestContext.Current.CancellationToken);
        Encoding.UTF8.GetString(bytes, 0, total).Should().Be("done");
    }

    [Fact]
    public async Task Adapter_DeadlineRemainsActiveWhileReadingResponse()
    {
        var transport = new FixedResponseTransport(id => new HttpTransportResponse(id, 200, Penghou.Http.Abstractions.HttpHeaders.Empty, new DelayedBody()));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await BaizeHttpTransportAdapter.SendAsync(transport, request,
            new HttpTransportLimits(timeout: TimeSpan.FromMilliseconds(50)), null, TestContext.Current.CancellationToken);
        var read = () => response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Adapter_BoundsUnknownLengthContentBeforeTransportDispatch()
    {
        var transport = new RecordingTransport();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/") { Content = new UnknownLengthContent("12345"u8.ToArray()) };
        var act = () => BaizeHttpTransportAdapter.SendAsync(transport, request,
            new HttpTransportLimits(maximumRequestBodyBytes: 4), null, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidDataException>();
        transport.Calls.Should().Be(0);
    }

    private sealed class FixedResponseTransport(Func<string, HttpTransportResponse> create) : IHttpTransport
    {
        public ValueTask<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(create(request.RequestId));
    }

    private sealed class FixedBody(byte[] bytes) : IHttpResponseBody
    {
        private int _offset;
        public ValueTask<HttpResponseChunk> ReadAsync(int maximumBytes, CancellationToken cancellationToken = default)
        {
            if (_offset == bytes.Length) return ValueTask.FromResult(new HttpResponseChunk(ReadOnlyMemory<byte>.Empty, true));
            var count = Math.Min(maximumBytes, bytes.Length - _offset);
            var chunk = new HttpResponseChunk(bytes.AsMemory(_offset, count), _offset + count == bytes.Length);
            _offset += count;
            return ValueTask.FromResult(chunk);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class DelayedBody : IHttpResponseBody
    {
        public async ValueTask<HttpResponseChunk> ReadAsync(int maximumBytes, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return new HttpResponseChunk(ReadOnlyMemory<byte>.Empty, true); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken) => stream.WriteAsync(bytes, cancellationToken).AsTask();
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private sealed class RecordingTransport : IHttpTransport
    {
        public int Calls { get; private set; }
        public ValueTask<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public byte[] ResponseBytes { get; set; } = "reply"u8.ToArray();
        public bool ResponseDisposed { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content is null ? null : new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken)) };
            foreach (var header in request.Headers) Request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            var content = new TrackingContent(ResponseBytes, () => ResponseDisposed = true);
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = content };
        }
    }

    private sealed class TrackingContent(byte[] bytes, Action disposed) : ByteArrayContent(bytes)
    { protected override void Dispose(bool disposing) { if (disposing) disposed(); base.Dispose(disposing); } }
}
