using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class BaizeBatchClientBaseTests
{
    [Fact]
    public void CredentialHelpers_OmitEmptyKeysAndApplyConfiguredKeys()
    {
        var empty = Client(string.Empty);
        using var emptyRequest = new HttpRequestMessage();
        empty.ApplyBearer(emptyRequest);
        empty.ApplyHeader(emptyRequest, "x-api-key");
        emptyRequest.Headers.Authorization.Should().BeNull();
        emptyRequest.Headers.Contains("x-api-key").Should().BeFalse();

        var configured = Client("secret");
        using var configuredRequest = new HttpRequestMessage();
        configured.ApplyBearer(configuredRequest);
        configured.ApplyHeader(configuredRequest, "x-api-key");
        configuredRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        configuredRequest.Headers.Authorization.Parameter.Should().Be("secret");
        configuredRequest.Headers.GetValues("x-api-key").Should().Equal("secret");
    }

    [Fact]
    public void SplitJsonl_RemovesOnlyBlankLines()
    {
        TestBatchClient.Split("first\n\n  \n second \n")
            .Should().Equal("first", " second ");
    }

    [Fact]
    public async Task Send_DeserializesSuccessfulResponse()
    {
        var client = Client(
            "key",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":42}", Encoding.UTF8, "application/json")
            });

        var result = await client.Send(new HttpRequestMessage(HttpMethod.Get, "https://test"));

        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task Send_ClassifiesHttpAndMalformedPayloadFailures()
    {
        var failed = Client(
            "key",
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request")
            });
        await failed.Invoking(client => client.Send(
                new HttpRequestMessage(HttpMethod.Get, "https://test")))
            .Should().ThrowAsync<LlmClientException>()
            .WithMessage("*HTTP 400*");

        var malformed = Client(
            "key",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json")
            });
        await malformed.Invoking(client => client.Send(
                new HttpRequestMessage(HttpMethod.Get, "https://test")))
            .Should().ThrowAsync<LlmClientException>()
            .WithMessage("*Failed to parse Test Provider batch response*");
    }

    [Fact]
    public async Task Send_RejectsJsonNullAsAnEmptyProtocolBody()
    {
        var client = Client(
            "key",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null")
            });

        await client.Invoking(value => value.Send(
                new HttpRequestMessage(HttpMethod.Get, "https://test")))
            .Should().ThrowAsync<LlmClientException>()
            .WithMessage("*empty WireResponse body*");
    }

    private static TestBatchClient Client(
        string apiKey,
        HttpResponseMessage? response = null) =>
        new(
            new TestHttpClientFactory(response ?? new HttpResponseMessage(HttpStatusCode.OK)),
            apiKey);

    private sealed class TestBatchClient(IHttpClientFactory factory, string apiKey)
        : BaizeBatchClientBase(
            "test",
            "model",
            factory,
            apiKey,
            new LlmEndpointCapabilities())
    {
        protected override string ProviderDisplayName => "Test Provider";

        public static IEnumerable<string> Split(string content) => SplitJsonl(content);
        public void ApplyBearer(HttpRequestMessage request) => ApplyBearerAuth(request);
        public void ApplyHeader(HttpRequestMessage request, string name) =>
            ApplyCredentialHeader(request, name);
        public Task<WireResponse> Send(HttpRequestMessage request) =>
            SendAsync<WireResponse>(
                request,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                CancellationToken.None);

        protected override void ApplyAuth(HttpRequestMessage request) { }
        public override Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ProviderBatchStatus> GetStatusAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task CancelAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record WireResponse(int Value);

    private sealed class TestHttpClientFactory(HttpResponseMessage response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(response));

        private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) => Task.FromResult(response);
        }
    }
}
