using System.Net;
using System.Text;
using FluentAssertions;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Tests;

/// <summary>
/// Exercises the shared generation transport mechanics: submission-aware
/// failure classification, JSON reading with detached elements, response
/// deserialization guards, and handle creation.
/// </summary>
public sealed class GenerationClientBaseTests
{
    private const string Endpoint = "test-endpoint";

    private static GenerationCapabilities Capabilities(
        GenerationFeature features = GenerationFeature.TextToImage,
        LlmContentTransport transports = LlmContentTransport.Uri) =>
        new()
        {
            Features = features,
            InputTransports = new HashSet<LlmContentTransport> { transports }
        };

    private static StubHttpHandler Handler(HttpStatusCode status, string body) => new(status, body);

    private static TestGenerationClient CreateClient(
        HttpMessageHandler handler,
        string apiKey = "key-1",
        GenerationCapabilities? capabilities = null) =>
        new(
            "runway",
            Endpoint,
            "gen-4",
            new StubHttpClientFactory(handler),
            apiKey,
            capabilities ?? Capabilities());

    // ---------- construction guards ----------

    [Theory]
    [InlineData(null, "e", "m")]
    [InlineData("p", null, "m")]
    [InlineData("p", "e", null)]
    public void Constructor_RejectsMissingIdentity(string? provider, string? endpointId, string? model)
    {
        var act = () => new TestGenerationClient(
            provider!,
            endpointId!,
            model!,
            new StubHttpClientFactory(Handler(HttpStatusCode.OK, "{}")),
            "",
            Capabilities());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsNullFactoryAndCapabilities()
    {
        var act = () => new TestGenerationClient(
            "p", "e", "m", null!, "", Capabilities());
        act.Should().Throw<ArgumentNullException>();

        var act2 = () => new TestGenerationClient(
            "p", "e", "m", new StubHttpClientFactory(Handler(HttpStatusCode.OK, "{}")), "", null!);
        act2.Should().Throw<ArgumentNullException>();
    }

    // ---------- SendAsync classification ----------

    [Fact]
    public async Task SendAsync_SubmissionTransportFailure_ClassifiesAsUnknownOutcome()
    {
        var client = CreateClient(new ThrowingHandler(new HttpRequestException("boom")));

        var act = async () => await client.SubmitTextToImageAsync(submission: true);

        var exception = (await act.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.UnknownSubmissionOutcome);
        exception.Message.Should().Contain("may or may not have accepted");
    }

    [Fact]
    public async Task SendAsync_StatusTransportFailure_ClassifiesAsProviderUnavailable()
    {
        var client = CreateClient(new ThrowingHandler(new HttpRequestException("boom")));

        var act = async () => await client.SubmitTextToImageAsync(submission: false, TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.ProviderUnavailable);
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatus_ThrowsClassifiedFailureWithBody()
    {
        var client = CreateClient(Handler(HttpStatusCode.TooManyRequests, """{"error":"slow down"}"""));

        var act = async () => await client.SubmitTextToImageAsync(submission: false, TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.RateLimited);
        exception.StatusCode.Should().Be(429);
        exception.ProviderStatus.Should().Contain("slow down");
    }

    [Fact]
    public async Task SendAsync_CallerCancellation_Propagates()
    {
        var client = CreateClient(new CancellingHandler());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.SubmitTextToImageAsync(
            submission: false,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ApplyAuth_DefaultBearer_IsAppliedWhenKeyPresent()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}", request => captured = request);
        var client = CreateClient(handler, apiKey: "secret");

        await client.SubmitTextToImageAsync(submission: false, TestContext.Current.CancellationToken);

        captured!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("secret");
    }

    [Fact]
    public async Task ApplyAuth_NoKey_OmitsHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}", request => captured = request);
        var client = CreateClient(handler, apiKey: "");

        await client.SubmitTextToImageAsync(submission: false, TestContext.Current.CancellationToken);

        captured!.Headers.Authorization.Should().BeNull();
    }

    // ---------- ReadJsonAsync / Deserialize guards ----------

    [Fact]
    public async Task ReadJsonAsync_ElementSurvivesResponseDisposal_CloneSemantics()
    {
        var client = CreateClient(Handler(HttpStatusCode.OK, """{"id":"op-1"}"""));

        var element = await client.ReadBodyAsync();
        element.GetProperty("id").GetString().Should().Be("op-1");
    }

    [Fact]
    public async Task ReadJsonAsync_MalformedBody_ThrowsGenerationFailedWithRawBody()
    {
        var client = CreateClient(Handler(HttpStatusCode.OK, "not json"));

        var act = async () => await client.ReadBodyAsync();

        var exception = (await act.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.GenerationFailed);
        exception.ProviderStatus.Should().Be("not json");
    }

    [Fact]
    public async Task Deserialize_EmptyPayload_ThrowsGenerationFailed()
    {
        var client = CreateClient(Handler(HttpStatusCode.OK, "null"));

        var act = async () => await client.DeserializeBodyAsync<string>();

        var exception = (await act.Should().ThrowAsync<BaizeException>()).Which;
        exception.Message.Should().Contain("empty response");
    }

    [Fact]
    public async Task ReadBytes_ReturnsUtf8Body()
    {
        var client = CreateClient(Handler(HttpStatusCode.OK, "bytes-body"));

        var bytes = await client.ReadBytesAsync();

        Encoding.UTF8.GetString(bytes).Should().Be("bytes-body");
    }

    // ---------- handles and validation delegation ----------

    [Fact]
    public void CreateHandle_PinsEndpointIdentity()
    {
        var client = CreateClient(Handler(HttpStatusCode.OK, "{}"));

        var handle = client.MakeHandle("task-9");

        handle.Provider.Should().Be("runway");
        handle.EndpointId.Should().Be(Endpoint);
        handle.Id.Should().Be("task-9");
        handle.Model.Should().Be("gen-4");
    }

    [Fact]
    public void ValidateRequest_DelegatesToCommonValidator()
    {
        var client = CreateClient(
            Handler(HttpStatusCode.OK, "{}"),
            capabilities: Capabilities(features: GenerationFeature.None));

        var act = () => client.ExposeValidate(
            new ImageGenerationRequest { Prompt = "x" });

        var exception = act.Should().Throw<BaizeException>().Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.UnsupportedCapability);
        exception.Message.Should().Contain(Endpoint);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }

    private sealed class CapturingHandler(
        HttpStatusCode status,
        string body,
        Action<HttpRequestMessage> capture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            capture(request);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
