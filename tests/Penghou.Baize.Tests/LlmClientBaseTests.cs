using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Tests;

public sealed class LlmClientBaseTests
{
    private static readonly LlmRequest Request =
        new([new LlmMessage("user", "hello")]);

    [Fact]
    public async Task ReadSseEventsAsync_HandlesMultilineDataEventResetAndEof()
    {
        const string source = """
            : comment
            event: message
            data: first
            data:second

            data:

            data: final
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));

        var events = await CollectAsync(
            ProbeClient.ReadSse(stream),
            TestContext.Current.CancellationToken);

        events.Should().Equal(
            ("message", "first\nsecond"),
            ((string?)null, "final"));
    }

    [Fact]
    public async Task ReadSseEventsAsync_RemovesOnlyOneOptionalAsciiSpace()
    {
        const string source = "event:  message  \n" +
                              "data:   leading\n" +
                              "data:\ttrailing  \n\n" +
                              "data:   \n\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));

        var events = await CollectAsync(
            ProbeClient.ReadSse(stream),
            TestContext.Current.CancellationToken);

        events.Should().Equal(
            (" message  ", "  leading\n\ttrailing  "),
            ((string?)null, "  "));
    }

    [Theory]
    [InlineData(null, "context", "Missing JSON for context.")]
    [InlineData(" ", "arguments", "Missing JSON for arguments.")]
    [InlineData("{", "payload", "Failed to parse payload: {")]
    public void ParseJsonElement_RejectsMissingOrMalformedJson(
        string? json,
        string context,
        string expectedMessage)
    {
        var action = () => ProbeClient.Parse(json, context);

        action.Should().Throw<LlmClientException>()
            .WithMessage(expectedMessage + "*");
    }

    [Fact]
    public void ParseJsonElement_ReturnsOwnedClone()
    {
        var element = ProbeClient.Parse("{\"value\":3}", "payload");

        element.GetProperty("value").GetInt32().Should().Be(3);
    }

    [Fact]
    public void ErrorFormatting_BoundsPayloadsAndRemovesSignedUrlParameters()
    {
        LlmJson.FormatForError(new string('x', 2_000))
            .Should().HaveLength(1_025).And.EndWith("…");
        LlmJson.FormatUrlForError(
                "https://cdn.test/output.mp4?token=secret#fragment")
            .Should().Be("https://cdn.test/output.mp4");
    }

    [Fact]
    public void ReadRateLimitInfo_CombinesOpenAiAnthropicAndRetryHeaders()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("x-ratelimit-remaining-requests", "4");
        response.Headers.Add("x-ratelimit-limit-requests", "10");
        response.Headers.Add("x-ratelimit-reset-requests", "1.5s");
        response.Headers.Add("anthropic-ratelimit-tokens-remaining", "90");
        response.Headers.Add("anthropic-ratelimit-tokens-limit", "100");
        var tokenReset = DateTimeOffset.UtcNow.AddMinutes(2);
        response.Headers.Add(
            "anthropic-ratelimit-tokens-reset",
            tokenReset.ToString("O"));
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            TimeSpan.FromSeconds(3));

        var before = DateTimeOffset.UtcNow;
        var result = ProbeClient.ReadRateLimit(response);

        result.Should().NotBeNull();
        result!.RequestsRemaining.Should().Be(4);
        result.RequestsLimit.Should().Be(10);
        result.RequestsResetAt.Should().BeAfter(before.AddSeconds(1));
        result.TokensRemaining.Should().Be(90);
        result.TokensLimit.Should().Be(100);
        result.TokensResetAt.Should().BeCloseTo(tokenReset, TimeSpan.FromSeconds(1));
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ReadRateLimitInfo_IgnoresMalformedHeadersAndSupportsRetryDate()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation(
            "x-ratelimit-remaining-requests",
            "many");
        response.Headers.TryAddWithoutValidation(
            "x-ratelimit-reset-tokens",
            "later");
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(1);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);

        var result = ProbeClient.ReadRateLimit(response);

        result.Should().NotBeNull();
        result!.RequestsRemaining.Should().BeNull();
        result.TokensResetAt.Should().BeNull();
        result.RetryAfter.Should().BeCloseTo(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ReadRateLimitInfo_ReturnsNullWithoutRecognizedHeaders()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        ProbeClient.ReadRateLimit(response).Should().BeNull();
    }

    [Fact]
    public async Task StreamAsync_AppendsRateLimitAfterProviderEvents()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("provider stream")
        };
        response.Headers.Add("x-ratelimit-remaining-tokens", "12");
        var client = new ProbeClient(new StaticHandler(response));

        var events = await CollectAsync(
            client.StreamAsync(Request, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        events.Should().HaveCount(2);
        events[0].Delta.Should().Be("provider stream");
        events[1].RateLimit!.TokensRemaining.Should().Be(12);
    }

    [Fact]
    public async Task StreamAsync_ReportsHttpFailureBodyAndRateLimit()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("quota exhausted")
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            TimeSpan.FromSeconds(5));
        var client = new ProbeClient(new StaticHandler(response));

        var action = () => CollectAsync(
            client.StreamAsync(Request),
            TestContext.Current.CancellationToken);

        var exception = (await action.Should().ThrowAsync<LlmClientException>())
            .Which;
        exception.StatusCode.Should().Be(429);
        exception.FailureKind.Should().Be(LlmClientFailureKind.RateLimit);
        exception.Message.Should().Contain("quota exhausted");
        exception.RateLimit!.RetryAfter.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StreamAsync_PropagatesRequestMappingFailureWithoutSending()
    {
        var handler = new CountingHandler();
        var client = new ProbeClient(handler) { FailRequestMapping = true };

        var action = () => CollectAsync(
            client.StreamAsync(Request),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("mapping failed");
        handler.Calls.Should().Be(0);
    }

    private sealed class ProbeClient(HttpMessageHandler handler)
        : LlmClientBase(
            "probe",
            new TestHttpClientFactory(handler),
            "secret",
            new LlmEndpointCapabilities(),
            "Test")
    {
        public bool FailRequestMapping { get; init; }

        public static IAsyncEnumerable<(string? EventType, string Data)> ReadSse(
            Stream stream) => ReadSseEventsAsync(stream, CancellationToken.None);

        public static JsonElement Parse(string? json, string context) =>
            ParseJsonElement(json, context);

        public static LlmRateLimitInfo? ReadRateLimit(HttpResponseMessage response) =>
            ReadRateLimitInfo(response);

        protected override HttpRequestMessage CreateHttpRequest(LlmRequest request)
        {
            if (FailRequestMapping)
                throw new InvalidOperationException("mapping failed");
            return new HttpRequestMessage(HttpMethod.Post, "https://example.test/chat");
        }

        protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);
            yield return new LlmStreamEvent(Delta: content);
        }
    }

    private static async Task<List<T>> CollectAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        var result = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
            result.Add(item);
        return result;
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StaticHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
