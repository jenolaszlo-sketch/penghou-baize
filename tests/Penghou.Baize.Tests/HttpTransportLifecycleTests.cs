using Penghou.Http.Abstractions;
using System.Net;
using System.Text;

namespace Penghou.Baize.Tests;

public sealed class HttpTransportLifecycleTests
{
    [Fact]
    public async Task ReusedLegacyHttpClientRemainsUsableForMultipleRequests()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var transport = new BaizeHttpTransport(new Factory(client));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var response = await transport.SendAsync(new HttpTransportRequest($"r{attempt}", "GET", new Uri("https://example.test/")), TestContext.Current.CancellationToken);
            var chunk = await response.Body.ReadAsync(8, TestContext.Current.CancellationToken);
            Assert.Equal("response", Encoding.UTF8.GetString(chunk.GetBytes()));
        }
        Assert.Equal(2, handler.Calls);
        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }

    [Fact]
    public async Task LegacyFactoryEndpointTimeoutCoversResponseBodyAfterHeaders()
    {
        var body = new WaitingBody();
        var transport = new ResponseTransport(body);
        using var client = BaizeHttp.CreateClientFactory(transport).WithRequestTimeout(TimeSpan.FromMilliseconds(50)).CreateClient(BaizeHttp.ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, body.Disposals);
    }

    [Fact]
    public async Task MultipartBodyIsVisibleAsBoundedTypedPartsBeforeDispatch()
    {
        var transport = new ResponseTransport(new EmptyBody());
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("payload"), "purpose");
        multipart.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "upload.bin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/") { Content = multipart };
        using var response = await BaizeHttpTransportAdapter.SendAsync(transport, request, TestContext.Current.CancellationToken);
        var body = Assert.IsType<HttpMultipartBody>(transport.Request!.Body);
        Assert.Equal(2, body.Parts.Count);
        Assert.Equal("purpose", body.Parts[0].Name);
        Assert.Equal("payload", Encoding.UTF8.GetString(body.Parts[0].Body.GetBytes()));
        Assert.Equal("upload.bin", body.Parts[1].FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, body.Parts[1].Body.GetBytes());
    }

    [Fact]
    public async Task TooManyMultipartPartsAreRejectedBeforeDispatch()
    {
        var transport = new ResponseTransport(new EmptyBody());
        using var multipart = new MultipartFormDataContent();
        for (var item = 0; item < 65; item++) multipart.Add(new StringContent("x"), "part");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/") { Content = multipart };
        await Assert.ThrowsAsync<InvalidDataException>(() => BaizeHttpTransportAdapter.SendAsync(transport, request, TestContext.Current.CancellationToken));
        Assert.Null(transport.Request);
    }

    [Fact]
    public async Task EndpointTimeoutCanExtendTheDefaultBackendTimeout()
    {
        var factory = new ShortTimeoutFactory();
        using var client = BaizeHttp.CreateClientFactory(new BaizeHttpTransport(factory))
            .WithRequestTimeout(TimeSpan.FromSeconds(5)).CreateClient(BaizeHttp.ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.Equal("extended", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(5), factory.Created!.Timeout);
    }

    private sealed class ShortTimeoutFactory : IHttpClientFactory
    {
        public HttpClient? Created;
        public HttpClient CreateClient(string name) => Created = new HttpClient(new DelayedHandler()) { Timeout = TimeSpan.FromMilliseconds(10) };
    }
    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("extended") };
        }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("response") });
        }
    }
    private sealed class ResponseTransport(IHttpResponseBody body) : IHttpTransport
    {
        public HttpTransportRequest? Request;
        public ValueTask<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(new HttpTransportResponse(request.RequestId, 200, Penghou.Http.Abstractions.HttpHeaders.Empty, body));
        }
    }
    private sealed class WaitingBody : IHttpResponseBody
    {
        public int Disposals;
        public async ValueTask<HttpResponseChunk> ReadAsync(int maximumBytes, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseChunk(ReadOnlyMemory<byte>.Empty, true);
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class EmptyBody : IHttpResponseBody
    {
        public ValueTask<HttpResponseChunk> ReadAsync(int maximumBytes, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HttpResponseChunk(ReadOnlyMemory<byte>.Empty, true));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
