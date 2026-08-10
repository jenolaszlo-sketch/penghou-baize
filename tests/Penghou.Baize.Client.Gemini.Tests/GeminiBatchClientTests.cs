using FluentAssertions;
using Penghou.Baize;
using Penghou.Baize.Gemini;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Gemini.Tests;

public sealed class GeminiBatchClientTests
{
    [Fact]
    public async Task SubmitAsync_UploadsJsonlThenCreatesBatch()
    {
        var handler = new BatchRecordingHandler(
            """{"file":{"name":"files/abc123","displayName":"batch-input.jsonl","mimeType":"application/jsonl"}}""",
            """{"name":"batches/123","done":false,"metadata":{"state":"BATCH_STATE_PENDING"}}""");
        var client = CreateClient(handler);

        var items = new List<BaizeBatchItem>
        {
            new("req-1", new LlmRequest([new LlmMessage("user", "Hello")])),
            new("req-2", new LlmRequest([new LlmMessage("user", "World")]))
        };

        var handle = await client.SubmitAsync(
            items,
            cancellationToken: TestContext.Current.CancellationToken);

        handle.ProviderId.Should().Be("Gemini");
        handle.BatchId.Should().Be("batches/123");
        handle.Metadata!["input_file_id"].Should().Be("files/abc123");

        handler.Requests.Should().HaveCount(3);

        var start = handler.Requests[0];
        start.Path.Should().Be("/upload/v1beta/files");
        start.Headers["X-Goog-Upload-Protocol"].Should().Equal("resumable");

        var upload = handler.Requests[1];
        upload.Path.Should().Be("/upload-session");
        upload.Body.Should().Contain("""key":"req-1""");
        upload.Body.Should().Contain("""key":"req-2""");
        upload.Body.Should().Contain("""text":"Hello""");
        upload.Body.Should().Contain("""text":"World""");
        upload.Headers.Should().ContainKey("x-goog-api-key")
            .WhoseValue.Should().Equal("test-key");

        var create = handler.Requests[2];
        create.Method.Should().Be(HttpMethod.Post);
        create.Path.Should().Be("/v1beta/models/gemini-2.5-flash:batchGenerateContent");

        using var createJson = JsonDocument.Parse(create.Body);
        var batch = createJson.RootElement.GetProperty("batch");
        batch.GetProperty("display_name").GetString().Should().Be("penghou-batch");
        batch.GetProperty("input_config")
            .GetProperty("file_name")
            .GetString()
            .Should().Be("files/abc123");
    }

    [Fact]
    public async Task SubmitAsync_ForwardsDisplayNameFromMetadata()
    {
        var handler = new BatchRecordingHandler(
            """{"file":{"name":"files/abc123"}}""",
            """{"name":"batches/123"}""");
        var client = CreateClient(handler);

        var options = new BatchSubmissionOptions
        {
            Metadata = new Dictionary<string, string>
            {
                ["display_name"] = "my-batch-job"
            }
        };

        await client.SubmitAsync(
            [new BaizeBatchItem("req-1", new LlmRequest([new LlmMessage("user", "Hi")]))],
            options,
            TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(handler.Requests[2].Body);
        json.RootElement.GetProperty("batch")
            .GetProperty("display_name")
            .GetString()
            .Should().Be("my-batch-job");
    }

    [Fact]
    public async Task SubmitAsync_RejectsEmptyItems()
    {
        var handler = new BatchRecordingHandler();
        var client = CreateClient(handler);

        var action = async () =>
            await client.SubmitAsync(
                [],
                cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubmitAsync_ValidatesEachItemBeforeTransmitting()
    {
        var handler = new BatchRecordingHandler();
        var client = CreateClient(
            handler,
            DefaultCapabilities with { NativeToolCalling = false });

        var action = async () =>
            await client.SubmitAsync(
                [
                    new BaizeBatchItem(
                        "req-1",
                        new LlmRequest(
                            [new LlmMessage("user", "Use tools")],
                            tools:
                            [
                                new LlmTool(
                                    "get_weather",
                                    "Gets the weather",
                                    """{"type":"object"}""")
                            ]))
                ],
                cancellationToken: TestContext.Current.CancellationToken);

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support native tool calling*");

        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("BATCH_STATE_PENDING", BaizeBatchState.Pending)]
    [InlineData("BATCH_STATE_RUNNING", BaizeBatchState.Running)]
    [InlineData("BATCH_STATE_CANCELLING", BaizeBatchState.Cancelling)]
    [InlineData("BATCH_STATE_SUCCEEDED", BaizeBatchState.Completed)]
    [InlineData("BATCH_STATE_FAILED", BaizeBatchState.Failed)]
    [InlineData("BATCH_STATE_CANCELLED", BaizeBatchState.Cancelled)]
    [InlineData("BATCH_STATE_EXPIRED", BaizeBatchState.Expired)]
    public async Task GetStatusAsync_MapsProviderState(
        string state,
        BaizeBatchState expected)
    {
        var handler = new BatchRecordingHandler(
            """{"name":"batches/123","done":false,"metadata":{"state":"__STATE__","batchStats":{"requestCount":"4","successfulRequestCount":"3","failedRequestCount":"1"}}}"""
                .Replace("__STATE__", state));
        var client = CreateClient(handler);

        var result = await client.GetStatusAsync(
            new ProviderBatchHandle("Gemini", "batches/123"),
            TestContext.Current.CancellationToken);

        result.State.Should().Be(expected);
        result.ProviderStatus.Should().Be(state);
        result.Total.Should().Be(4);
        result.Completed.Should().Be(3);
        result.Failed.Should().Be(1);
    }

    [Fact]
    public async Task GetStatusAsync_DefaultsToCompletedWhenOperationDone()
    {
        var handler = new BatchRecordingHandler(
            """{"name":"batches/123","done":true}""");
        var client = CreateClient(handler);

        var result = await client.GetStatusAsync(
            new ProviderBatchHandle("Gemini", "batches/123"),
            TestContext.Current.CancellationToken);

        result.State.Should().Be(BaizeBatchState.Completed);
    }

    [Fact]
    public async Task GetResultsAsync_ParsesSucceededAndFailedItems()
    {
        var handler = new BatchRecordingHandler(
            """{"name":"batches/123","done":true,"metadata":{"state":"BATCH_STATE_SUCCEEDED"},"response":{"state":"BATCH_STATE_SUCCEEDED","output":{"responsesFile":"files/out"}}}""",
            """
            {"key":"req-1","response":{"candidates":[{"content":{"parts":[{"text":"Hello"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":5,"totalTokenCount":15}}}
            {"key":"req-2","error":{"code":429,"message":"quota exceeded","status":"RESOURCE_EXHAUSTED"}}
            """);
        var client = CreateClient(handler);

        var results = await client.GetResultsAsync(
            new ProviderBatchHandle("Gemini", "batches/123"),
            TestContext.Current.CancellationToken);

        handler.Requests[1].Method.Should().Be(HttpMethod.Get);
        handler.Requests[1].Path.Should().Be("/v1beta/files/out");

        results.Should().HaveCount(2);

        var succeeded = results.Single(result => result.RequestId == "req-1");
        succeeded.State.Should().Be(BaizeBatchItemState.Succeeded);
        succeeded.Response!.Content.Should().Be("Hello");
        succeeded.Response.FinishReason.Should().Be("stop");
        succeeded.Response.Usage!.TotalTokens.Should().Be(15);

        var failed = results.Single(result => result.RequestId == "req-2");
        failed.State.Should().Be(BaizeBatchItemState.Failed);
        failed.Error!.Message.Should().Be("quota exceeded");
        failed.Error.FailureKind.Should().Be(LlmClientFailureKind.RateLimit);
        failed.Error.StatusCode.Should().Be(429);
        failed.Error.ProviderStatus.Should().Be("RESOURCE_EXHAUSTED");
    }

    [Fact]
    public async Task GetResultsAsync_MapsToolCallsAndThinking()
    {
        var handler = new BatchRecordingHandler(
            """{"name":"batches/123","done":true,"metadata":{"state":"BATCH_STATE_SUCCEEDED"},"response":{"state":"BATCH_STATE_SUCCEEDED","output":{"responsesFile":"files/out"}}}""",
            """{"key":"req-1","response":{"candidates":[{"content":{"parts":[{"thought":true,"text":"reasoning here","thoughtSignature":"sig-1"},{"text":"answer text"},{"functionCall":{"id":"fc_1","name":"get_weather","args":{"city":"Paris"}}}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":12,"candidatesTokenCount":8,"totalTokenCount":20}}}""");
        var client = CreateClient(handler);

        var results = await client.GetResultsAsync(
            new ProviderBatchHandle("Gemini", "batches/123"),
            TestContext.Current.CancellationToken);

        var result = results.Single();
        result.State.Should().Be(BaizeBatchItemState.Succeeded);
        result.Response!.Content.Should().Be("answer text");
        result.Response.Reasoning.Should().Be("reasoning here");
        result.Response.ReasoningContinuation!.Values["thoughtSignature"].Should().Be("sig-1");
        result.Response.ToolCalls.Should().HaveCount(1);
        result.Response.ToolCalls![0].Id.Should().Be("fc_1");
        result.Response.ToolCalls[0].Name.Should().Be("get_weather");
        result.Response.ToolCalls[0].ArgumentsJson.Should().Contain("\"city\":\"Paris\"");
    }

    [Fact]
    public async Task GetResultsAsync_ParsesInlinedResponses()
    {
        var handler = new BatchRecordingHandler(
            """{"name":"batches/123","done":true,"metadata":{"state":"BATCH_STATE_SUCCEEDED"},"response":{"state":"BATCH_STATE_SUCCEEDED","output":{"inlinedResponses":{"inlinedResponses":[{"key":"req-1","response":{"candidates":[{"content":{"parts":[{"text":"inline"}]},"finishReason":"STOP"}]}},{"key":"req-2","error":{"code":400,"message":"bad request","status":"INVALID_ARGUMENT"}}]}}}}""");
        var client = CreateClient(handler);

        var results = await client.GetResultsAsync(
            new ProviderBatchHandle("Gemini", "batches/123"),
            TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);

        var succeeded = results.Single(result => result.RequestId == "req-1");
        succeeded.State.Should().Be(BaizeBatchItemState.Succeeded);
        succeeded.Response!.Content.Should().Be("inline");

        var failed = results.Single(result => result.RequestId == "req-2");
        failed.State.Should().Be(BaizeBatchItemState.Failed);
        failed.Error!.FailureKind.Should().Be(LlmClientFailureKind.InvalidRequest);
        failed.Error.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetResultsAsync_ThrowsWhenNoResultsYet()
    {
        var handler = new BatchRecordingHandler(
            """{"name":"batches/123","done":false,"metadata":{"state":"BATCH_STATE_RUNNING"}}""");
        var client = CreateClient(handler);

        var action = async () =>
            await client.GetResultsAsync(
                new ProviderBatchHandle("Gemini", "batches/123"),
                TestContext.Current.CancellationToken);

        await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage("*has no results yet*");
    }

    [Fact]
    public async Task GetResultsAsync_ThrowsOnMalformedResultLine()
    {
        var handler = new BatchRecordingHandler(
            """{"name":"batches/123","done":true,"metadata":{"state":"BATCH_STATE_SUCCEEDED"},"response":{"state":"BATCH_STATE_SUCCEEDED","output":{"responsesFile":"files/out"}}}""",
            "this is not json");
        var client = CreateClient(handler);

        var action = async () =>
            await client.GetResultsAsync(
                new ProviderBatchHandle("Gemini", "batches/123"),
                TestContext.Current.CancellationToken);

        await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage("*Failed to parse Gemini batch result line*");
    }

    [Fact]
    public async Task GetResultsAsync_RejectsMissingCorrelationKey()
    {
        var handler = new BatchRecordingHandler(
            """{"name":"batches/123","done":true,"response":{"output":{"responsesFile":"files/out"}}}""",
            """{"response":{"candidates":[]}}""");
        var client = CreateClient(handler);

        var action = async () => await client.GetResultsAsync(
            new ProviderBatchHandle("Gemini", "batches/123"),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<LlmClientException>()
            .WithMessage("*has no correlation key*");
    }

    [Fact]
    public async Task CancelAsync_PostsToCancelEndpoint()
    {
        var handler = new BatchRecordingHandler("{}");
        var client = CreateClient(handler);

        await client.CancelAsync(
            new ProviderBatchHandle("Gemini", "batches/123"),
            TestContext.Current.CancellationToken);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be("/v1beta/batches/123:cancel");
    }

    [Fact]
    public async Task CancelAsync_ThrowsWhenCancellationNotAdvertised()
    {
        var handler = new BatchRecordingHandler();
        var client = CreateClient(
            handler,
            DefaultCapabilities with
            {
                Batch = BatchCapabilities.NativeBatch | BatchCapabilities.Polling
            });

        var action = async () =>
            await client.CancelAsync(
                new ProviderBatchHandle("Gemini", "batches/123"),
                TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void Provider_CreateBatchClient_ReturnsConfiguredBatchClient()
    {
        var provider = new GeminiClientProvider();

        var context = new LlmClientProviderContext(
            Model: "gemini-2.5-flash",
            HttpClientFactory: new TestHttpClientFactory(
                new HttpClient(new BatchRecordingHandler())),
            ApiKey: "secret",
            BaseUrl: "https://generativelanguage.googleapis.com",
            Capabilities: provider.DefaultCapabilities,
            Settings: new Dictionary<string, string>());

        var batchClient = provider.CreateBatchClient(context);

        batchClient.Should().NotBeNull();
        batchClient!.ProviderId.Should().Be("Gemini");
        batchClient.Capabilities.Should().Be(
            BatchCapabilities.NativeBatch |
            BatchCapabilities.Polling |
            BatchCapabilities.Cancellation);
    }

    [Fact]
    public void Provider_CreateBatchClient_ReturnsNullWhenNativeBatchUnavailable()
    {
        var provider = new GeminiClientProvider();

        var context = new LlmClientProviderContext(
            Model: "gemini-2.5-flash",
            HttpClientFactory: new TestHttpClientFactory(
                new HttpClient(new BatchRecordingHandler())),
            ApiKey: "secret",
            BaseUrl: "https://generativelanguage.googleapis.com",
            Capabilities: provider.DefaultCapabilities with { Batch = BatchCapabilities.None },
            Settings: new Dictionary<string, string>());

        provider.CreateBatchClient(context).Should().BeNull();
    }

    private static LlmEndpointCapabilities DefaultCapabilities =>
        new()
        {
            NativeToolCalling = true,
            ParallelToolCalls = true,
            NativeStructuredOutput = true,
            StructuredOutputViaTool = false,
            Thinking = true,
            ThinkingDisable = true,
            StreamingToolCallArguments = true,
            Batch =
                BatchCapabilities.NativeBatch |
                BatchCapabilities.Polling |
                BatchCapabilities.Cancellation,
            SupportedThinkingEfforts =
                new HashSet<LlmThinkingEffort>
                {
                    LlmThinkingEffort.Low,
                    LlmThinkingEffort.Medium,
                    LlmThinkingEffort.High,
                    LlmThinkingEffort.Max
                }
        };

    private static GeminiBatchClient CreateClient(
        BatchRecordingHandler handler,
        LlmEndpointCapabilities? capabilities = null) =>
        new(
            httpClientFactory: new TestHttpClientFactory(
                new HttpClient(handler)),
            model: "gemini-2.5-flash",
            apiKey: "test-key",
            baseUrl: "https://generativelanguage.googleapis.com",
            capabilities: capabilities ?? DefaultCapabilities);

    private sealed class TestHttpClientFactory(
        HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name) =>
            client;
    }

    private sealed class BatchRecordingHandler(
        params string[] responseBodies)
        : HttpMessageHandler
    {
        private readonly Queue<string> _responses =
            new(responseBodies);

        public List<RecordedRequest> Requests { get; } = [];

        public sealed record RecordedRequest(
            HttpMethod Method,
            string Path,
            string Body,
            IReadOnlyDictionary<string, IEnumerable<string>> Headers);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);

            var headers = request.Headers.ToDictionary(
                header => header.Key,
                header => (IEnumerable<string>)header.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                body,
                headers));

            if (request.Headers.TryGetValues(
                    "X-Goog-Upload-Command",
                    out var uploadCommands) &&
                uploadCommands.Contains("start", StringComparer.OrdinalIgnoreCase))
            {
                var startResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                startResponse.Headers.TryAddWithoutValidation(
                    "X-Goog-Upload-URL",
                    "https://upload.example/upload-session");
                return startResponse;
            }

            var responseBody = _responses.TryDequeue(out var queued)
                ? queued
                : "{}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
