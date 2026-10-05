using Penghou.Http.Abstractions;
using System.Net;

namespace Penghou.Baize.Tests;

public sealed class HttpTransportFailureTests
{
    [Theory]
    [InlineData("identity")]
    [InlineData("headers")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("aggregate")]
    public async Task InvalidReplacementResponsesAreRejectedAndDisposed(string failure)
    {
        var body = new Body(failure);
        var transport = new Replacement(body, failure);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        var limits = new HttpTransportLimits(maximumResponseBodyBytes: 3, maximumChunkBytes: 2, maximumResponseHeaderBytes: 32);
        if (failure is "identity" or "headers")
        {
            await Assert.ThrowsAnyAsync<Exception>(() => BaizeHttpTransportAdapter.SendAsync(transport, request, limits, null, TestContext.Current.CancellationToken));
        }
        else
        {
            using var response = await BaizeHttpTransportAdapter.SendAsync(transport, request, limits, null, TestContext.Current.CancellationToken);
            if (failure == "empty") await Assert.ThrowsAsync<ArgumentException>(() => response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            else await Assert.ThrowsAsync<InvalidDataException>(() => response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        }
        Assert.Equal(1, body.Disposals);
    }

    [Fact]
    public async Task DefaultMultipartFramingIsIncludedInRequestLimit()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var transport = new BaizeHttpTransport(new Factory(client));
        var parts = new HttpMultipartBody(new[]
        {
            new HttpMultipartPart("plain", new HttpBinaryBody(new byte[] { 1 }, "application/octet-stream")),
            new HttpMultipartPart("file", new HttpBinaryBody(new byte[] { 2 }, "application/octet-stream"), "a.bin")
        });
        var request = new HttpTransportRequest("multipart", "POST", new Uri("https://example.test/"), parts,
            limits: new HttpTransportLimits(maximumRequestBodyBytes: 2));
        await Assert.ThrowsAsync<InvalidDataException>(() => transport.SendAsync(request, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, handler.Calls);
        var allowed = new HttpTransportRequest("allowed", "POST", request.Uri, parts);
        await using var response = await transport.SendAsync(allowed, TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DefaultResponseHeaderLimitRejectsBeforeBodyOwnershipTransfers()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var transport = new BaizeHttpTransport(new Factory(client));
        var request = new HttpTransportRequest("headers", "GET", new Uri("https://example.test/"),
            limits: new HttpTransportLimits(maximumResponseHeaderBytes: 32));
        await Assert.ThrowsAsync<InvalidDataException>(() => transport.SendAsync(request, TestContext.Current.CancellationToken).AsTask());
        Assert.True(handler.Disposed);
    }

    private sealed class Replacement(Body body, string failure) : IHttpTransport
    {
        public ValueTask<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HttpTransportResponse(failure == "identity" ? "different" : request.RequestId, 200,
                failure == "headers" ? new Penghou.Http.Abstractions.HttpHeaders(new Dictionary<string, IReadOnlyList<string>> { ["X-Large"] = new[] { new string('x', 80) } }) : Penghou.Http.Abstractions.HttpHeaders.Empty, body));
    }
    private sealed class Body(string failure) : IHttpResponseBody
    {
        public int Disposals;
        public ValueTask<HttpResponseChunk> ReadAsync(int maximumBytes, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HttpResponseChunk(failure == "empty" ? ReadOnlyMemory<byte>.Empty : new byte[failure == "oversized" ? maximumBytes + 1 : 2], false));
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new TrackedContent(() => Disposed = true) };
            response.Headers.Add("X-Large", new string('x', 80));
            return Task.FromResult(response);
        }
    }
    private sealed class TrackedContent(Action disposed) : ByteArrayContent(Array.Empty<byte>())
    {
        protected override void Dispose(bool disposing) { if (disposing) disposed(); base.Dispose(disposing); }
    }
}
