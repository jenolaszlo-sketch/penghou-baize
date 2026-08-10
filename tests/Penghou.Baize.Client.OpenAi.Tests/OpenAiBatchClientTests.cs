using FluentAssertions;
using Penghou.Baize;
using Penghou.Baize.OpenAi;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.OpenAi.Tests;

public sealed class OpenAiBatchClientTests
{
    [Fact]
    public async Task SubmitAsync_UploadsJsonlThenCreatesBatch()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"file-1","object":"file","bytes":120,"purpose":"batch","filename":"batch-input.jsonl","status":"uploaded"}""",
            """{"id":"batch-1","object":"batch","status":"validating","input_file_id":"file-1","request_counts":{"total":2,"completed":0,"failed":0}}""");
        var client = CreateClient(handler);

        var items = new List<BaizeBatchItem>
        {
            new("req-1", new LlmRequest([new LlmMessage("user", "Hello")])),
            new("req-2", new LlmRequest([new LlmMessage("user", "World")]))
        };

        var handle = await client.SubmitAsync(
            items,
            cancellationToken: TestContext.Current.CancellationToken);

        handle.ProviderId.Should().Be("OpenAi");
        handle.BatchId.Should().Be("batch-1");
        handle.Metadata!["input_file_id"].Should().Be("file-1");

        handler.Requests.Should().HaveCount(2);

        var upload = handler.Requests[0];
        upload.Method.Should().Be(HttpMethod.Post);
        upload.Path.Should().Be("/v1/files");
        upload.Body.Should().Contain("batch-input.jsonl");
        upload.Body.Should().Contain("""custom_id":"req-1""");
        upload.Body.Should().Contain("""custom_id":"req-2""");
        upload.Body.Should().Contain("""method":"POST""");
        upload.Body.Should().Contain("""url":"/v1/chat/completions""");
        upload.Body.Should().Contain("""model":"gpt-4o-mini""");
        upload.Body.Should().NotContain("\"stream\":true");
        upload.Headers.Should().ContainKey("Authorization")
            .WhoseValue.Should().Equal("Bearer secret");

        var create = handler.Requests[1];
        create.Method.Should().Be(HttpMethod.Post);
        create.Path.Should().Be("/v1/batches");

        using var createJson = JsonDocument.Parse(create.Body);
        var root = createJson.RootElement;
        root.GetProperty("input_file_id").GetString().Should().Be("file-1");
        root.GetProperty("endpoint").GetString().Should().Be("/v1/chat/completions");
        root.GetProperty("completion_window").GetString().Should().Be("24h");
    }

    [Fact]
    public async Task SubmitAsync_ForwardsIdempotencyKeyAndMetadata()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"file-1","status":"uploaded"}""",
            """{"id":"batch-1","status":"validating","input_file_id":"file-1"}""");
        var client = CreateClient(handler);

        var options = new BatchSubmissionOptions
        {
            IdempotencyKey = "key-1",
            Metadata = new Dictionary<string, string>
            {
                ["project"] = "penghou"
            }
        };

        await client.SubmitAsync(
            [new BaizeBatchItem("req-1", new LlmRequest([new LlmMessage("user", "Hi")]))],
            options,
            TestContext.Current.CancellationToken);

        var create = handler.Requests[1];
        create.Headers["Idempotency-Key"].Should().Equal("key-1");

        using var json = JsonDocument.Parse(create.Body);
        json.RootElement.GetProperty("metadata")
            .GetProperty("project")
            .GetString()
            .Should().Be("penghou");
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
    [InlineData("validating", BaizeBatchState.Pending)]
    [InlineData("queued", BaizeBatchState.Pending)]
    [InlineData("in_progress", BaizeBatchState.Running)]
    [InlineData("finalizing", BaizeBatchState.Running)]
    [InlineData("completed", BaizeBatchState.Completed)]
    [InlineData("failed", BaizeBatchState.Failed)]
    [InlineData("expired", BaizeBatchState.Expired)]
    [InlineData("cancelling", BaizeBatchState.Cancelling)]
    [InlineData("cancelled", BaizeBatchState.Cancelled)]
    public async Task GetStatusAsync_MapsProviderState(
        string status,
        BaizeBatchState expected)
    {
        var handler = new BatchRecordingHandler(
            """{"id":"batch-1","object":"batch","status":"__STATUS__","request_counts":{"total":4,"completed":3,"failed":1}}"""
                .Replace("__STATUS__", status));
        var client = CreateClient(handler);

        var result = await client.GetStatusAsync(
            new ProviderBatchHandle("OpenAi", "batch-1"),
            TestContext.Current.CancellationToken);

        result.State.Should().Be(expected);
        result.ProviderStatus.Should().Be(status);
        result.Total.Should().Be(4);
        result.Completed.Should().Be(3);
        result.Failed.Should().Be(1);
    }

    [Fact]
    public async Task GetResultsAsync_ParsesSucceededAndFailedItems()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"batch-1","object":"batch","status":"completed","input_file_id":"file-1","output_file_id":"file-out-1","request_counts":{"total":2,"completed":1,"failed":1}}""",
            """
            {"id":"batch_req_a","custom_id":"req-1","response":{"status_code":200,"request_id":"req_x","body":{"id":"chatcmpl-1","object":"chat.completion","model":"gpt-4o-mini","choices":[{"index":0,"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":2,"total_tokens":5}}}}
            {"id":"batch_req_b","custom_id":"req-2","response":{"status_code":400,"request_id":"req_y","body":{"error":{"message":"bad request","type":"invalid_request_error","param":null,"code":null}}}}
            """);
        var client = CreateClient(handler);

        var results = await client.GetResultsAsync(
            new ProviderBatchHandle("OpenAi", "batch-1"),
            TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);

        var succeeded = results.Single(result => result.RequestId == "req-1");
        succeeded.State.Should().Be(BaizeBatchItemState.Succeeded);
        succeeded.Response!.Content.Should().Be("Hello");
        succeeded.Response.FinishReason.Should().Be("stop");
        succeeded.Response.Usage!.TotalTokens.Should().Be(5);

        var failed = results.Single(result => result.RequestId == "req-2");
        failed.State.Should().Be(BaizeBatchItemState.Failed);
        failed.Error!.Message.Should().Be("bad request");
        failed.Error.FailureKind.Should().Be(LlmClientFailureKind.InvalidRequest);
        failed.Error.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetResultsAsync_MapsToolCalls()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"batch-1","object":"batch","status":"completed","output_file_id":"file-out-1"}""",
            """{"id":"batch_req_a","custom_id":"req-1","response":{"status_code":200,"request_id":"req_x","body":{"id":"chatcmpl-1","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Paris\"}"}}]},"finish_reason":"tool_calls"}]}}}""");
        var client = CreateClient(handler);

        var results = await client.GetResultsAsync(
            new ProviderBatchHandle("OpenAi", "batch-1"),
            TestContext.Current.CancellationToken);

        handler.Requests[1].Method.Should().Be(HttpMethod.Get);
        handler.Requests[1].Path.Should().Be("/v1/files/file-out-1/content");

        var result = results.Single();
        result.State.Should().Be(BaizeBatchItemState.Succeeded);
        result.Response!.ToolCalls.Should().HaveCount(1);
        result.Response.ToolCalls![0].Id.Should().Be("call_1");
        result.Response.ToolCalls[0].Name.Should().Be("get_weather");
        result.Response.ToolCalls[0].ArgumentsJson.Should().Be("{\"city\":\"Paris\"}");
        result.Response.FinishReason.Should().Be("tool_calls");
    }

    [Fact]
    public async Task GetResultsAsync_ThrowsWhenNoOutputFileYet()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"batch-1","object":"batch","status":"in_progress","input_file_id":"file-1"}""");
        var client = CreateClient(handler);

        var action = async () =>
            await client.GetResultsAsync(
                new ProviderBatchHandle("OpenAi", "batch-1"),
                TestContext.Current.CancellationToken);

        await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage("*has no output or error file yet*");
    }

    [Fact]
    public async Task GetResultsAsync_MergesOutputAndErrorFiles()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"batch-1","status":"completed","output_file_id":"file-out","error_file_id":"file-errors"}""",
            """{"custom_id":"ok","response":{"status_code":200,"body":{"choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}}}""",
            """{"custom_id":"bad","error":{"message":"invalid","type":"invalid_request_error"}}""");
        var client = CreateClient(handler);

        var results = await client.GetResultsAsync(
            new ProviderBatchHandle("OpenAi", "batch-1"),
            TestContext.Current.CancellationToken);

        results.Select(result => result.RequestId).Should().Equal("ok", "bad");
        results[0].State.Should().Be(BaizeBatchItemState.Succeeded);
        results[1].State.Should().Be(BaizeBatchItemState.Failed);
        handler.Requests.Select(request => request.Path).Should().ContainInOrder(
            "/v1/batches/batch-1",
            "/v1/files/file-out/content",
            "/v1/files/file-errors/content");
    }

    [Fact]
    public async Task GetStatusAsync_RejectsHandleForDifferentProvider()
    {
        var client = CreateClient(new BatchRecordingHandler());

        var action = async () => await client.GetStatusAsync(
            new ProviderBatchHandle("Claude", "batch-1"),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*belongs to provider 'Claude'*");
    }

    [Fact]
    public async Task GetResultsAsync_RejectsMissingCorrelationId()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"batch-1","status":"completed","output_file_id":"file-out"}""",
            """{"response":{"status_code":400,"body":{"error":{"message":"bad"}}}}""");
        var client = CreateClient(handler);

        var action = async () => await client.GetResultsAsync(
            new ProviderBatchHandle("OpenAi", "batch-1"),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<LlmClientException>()
            .WithMessage("*has no custom_id*");
    }

    [Fact]
    public async Task GetResultsAsync_ThrowsOnMalformedResultLine()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"batch-1","object":"batch","status":"completed","output_file_id":"file-out-1"}""",
            "this is not json");
        var client = CreateClient(handler);

        var action = async () =>
            await client.GetResultsAsync(
                new ProviderBatchHandle("OpenAi", "batch-1"),
                TestContext.Current.CancellationToken);

        await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage("*Failed to parse OpenAI batch result line*");
    }

    [Fact]
    public async Task CancelAsync_PostsToCancelEndpoint()
    {
        var handler = new BatchRecordingHandler(
            """{"id":"batch-1","object":"batch","status":"cancelling"}""");
        var client = CreateClient(handler);

        await client.CancelAsync(
            new ProviderBatchHandle("OpenAi", "batch-1"),
            TestContext.Current.CancellationToken);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be("/v1/batches/batch-1/cancel");
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
                new ProviderBatchHandle("OpenAi", "batch-1"),
                TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void Provider_CreateBatchClient_ReturnsConfiguredBatchClient()
    {
        var provider = new OpenAiClientProvider();

        var context = new LlmClientProviderContext(
            Model: "gpt-4o-mini",
            HttpClientFactory: new TestHttpClientFactory(
                new HttpClient(new BatchRecordingHandler())),
            ApiKey: "secret",
            BaseUrl: "https://openai.test/v1",
            Capabilities: provider.DefaultCapabilities,
            Settings: new Dictionary<string, string>());

        var batchClient = provider.CreateBatchClient(context);

        batchClient.Should().NotBeNull();
        batchClient!.ProviderId.Should().Be("OpenAi");
        batchClient.Capabilities.Should().Be(
            BatchCapabilities.NativeBatch |
            BatchCapabilities.Polling |
            BatchCapabilities.Cancellation);
    }

    [Fact]
    public void Provider_CreateBatchClient_ReturnsNullWhenNativeBatchUnavailable()
    {
        var provider = new OpenAiClientProvider();

        var context = new LlmClientProviderContext(
            Model: "gpt-4o-mini",
            HttpClientFactory: new TestHttpClientFactory(
                new HttpClient(new BatchRecordingHandler())),
            ApiKey: "secret",
            BaseUrl: "https://openai.test/v1",
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
            ThinkingDisable = false,
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
                    LlmThinkingEffort.High
                }
        };

    private static OpenAiBatchClient CreateClient(
        BatchRecordingHandler handler,
        LlmEndpointCapabilities? capabilities = null) =>
        new(
            model: "gpt-4o-mini",
            httpClientFactory: new TestHttpClientFactory(
                new HttpClient(handler)),
            apiKey: "secret",
            baseUrl: "https://openai.test/v1",
            capabilities: capabilities ?? DefaultCapabilities,
            dialect: OpenAiDialect.Standard);

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
