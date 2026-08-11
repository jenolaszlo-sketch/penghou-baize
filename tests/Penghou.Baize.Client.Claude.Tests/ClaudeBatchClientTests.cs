using FluentAssertions;
using Penghou.Baize;
using Penghou.Baize.Claude;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Claude.Tests;

public sealed class ClaudeBatchClientTests
{
    [Fact]
    public async Task SubmitAsync_SendsInlineRequests()
    {
        var handler = new ClaudeBatchRecordingHandler(
            """{"id":"msgbatch_1","type":"message_batch","processing_status":"in_progress","request_counts":{"total":2,"processed":0,"succeeded":0,"errored":0,"canceled":0,"expired":0}}""");
        var client = CreateClient(handler);

        var items = new List<BaizeBatchItem>
        {
            new("req-1", new LlmRequest([new LlmMessage("user", "Hello")], maxTokens: 100)),
            new("req-2", new LlmRequest([new LlmMessage("user", "World")], maxTokens: 200))
        };

        var handle = await client.SubmitAsync(
            items,
            cancellationToken: TestContext.Current.CancellationToken);

        handle.ProviderId.Should().Be("Claude");
        handle.BatchId.Should().Be("msgbatch_1");

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be("/v1/messages/batches");
        request.Headers["x-api-key"].Should().Be("secret");
        request.Headers["anthropic-version"].Should().Be("2023-06-01");

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        var requests = root.GetProperty("requests");
        requests.GetArrayLength().Should().Be(2);

        requests[0].GetProperty("custom_id").GetString().Should().Be("req-1");
        var params0 = requests[0].GetProperty("params");
        params0.GetProperty("model").GetString().Should().Be("claude-test");
        params0.GetProperty("max_tokens").GetInt32().Should().Be(100);
        params0.TryGetProperty("stream", out _).Should().BeFalse();

        requests[1].GetProperty("custom_id").GetString().Should().Be("req-2");
        requests[1].GetProperty("params")
            .GetProperty("max_tokens")
            .GetInt32()
            .Should().Be(200);
    }

    [Fact]
    public async Task SubmitAsync_ForwardsMetadata()
    {
        var handler = new ClaudeBatchRecordingHandler(
            """{"id":"msgbatch_1","processing_status":"in_progress"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            [new BaizeBatchItem("req-1", new LlmRequest([new LlmMessage("user", "Hi")]))],
            new BatchSubmissionOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["project"] = "penghou"
                }
            },
            TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        json.RootElement.GetProperty("metadata")
            .GetProperty("project")
            .GetString()
            .Should().Be("penghou");
    }

    [Fact]
    public async Task SubmitAsync_RejectsEmptyItems()
    {
        var handler = new ClaudeBatchRecordingHandler();
        var client = CreateClient(handler);

        var action = async () =>
            await client.SubmitAsync(
                [],
                cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubmitAsync_ValidatesItemsBeforeTransmitting()
    {
        var handler = new ClaudeBatchRecordingHandler();
        var client = CreateClient(handler);
        var request = new LlmRequest(
            [new LlmMessage("user", "Use tools")],
            tools:
            [
                new LlmTool(
                    "get_weather",
                    "Gets the weather",
                    """{"type":"object"}""")
            ],
            responseFormat: LlmResponseFormat.JsonSchema(
                """{"type":"object"}"""));

        var action = async () =>
            await client.SubmitAsync(
                [new BaizeBatchItem("req-1", request)],
                cancellationToken: TestContext.Current.CancellationToken);

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support combining tools with structured output*");

        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("in_progress", 0, 0, 0, BaizeBatchState.Pending)]
    [InlineData("processing", 2, 1, 0, BaizeBatchState.Running)]
    [InlineData("ended", 2, 2, 0, BaizeBatchState.Completed)]
    [InlineData("ended", 2, 1, 1, BaizeBatchState.PartiallyCompleted)]
    [InlineData("ended", 2, 0, 2, BaizeBatchState.Failed)]
    [InlineData("canceled", 2, 0, 0, BaizeBatchState.Cancelled)]
    [InlineData("expired", 2, 0, 0, BaizeBatchState.Expired)]
    public async Task GetStatusAsync_MapsProviderState(
        string status,
        int total,
        int succeeded,
        int errored,
        BaizeBatchState expected)
    {
        var handler = new ClaudeBatchRecordingHandler(
            """{"id":"msgbatch_1","type":"message_batch","processing_status":"__STATUS__","request_counts":{"total":__TOTAL__,"processed":__PROCESSED__,"succeeded":__SUCCEEDED__,"errored":__ERRORED__,"canceled":0,"expired":0}}"""
                .Replace("__STATUS__", status)
                .Replace("__TOTAL__", total.ToString())
                .Replace("__PROCESSED__", (succeeded + errored).ToString())
                .Replace("__SUCCEEDED__", succeeded.ToString())
                .Replace("__ERRORED__", errored.ToString()));
        var client = CreateClient(handler);

        var result = await client.GetStatusAsync(
            new ProviderBatchHandle("Claude", "msgbatch_1"),
            TestContext.Current.CancellationToken);

        result.State.Should().Be(expected);
        result.ProviderStatus.Should().Be(status);
        result.Total.Should().Be(total);
        result.Completed.Should().Be(succeeded);
        result.Failed.Should().Be(errored);
    }

    [Fact]
    public async Task GetResultsAsync_NormalizesOutcomes()
    {
        var handler = new ClaudeBatchRecordingHandler(
            """
            {"custom_id":"req-1","result":{"type":"succeeded","message":{"content":[{"type":"thinking","thinking":"let me think","signature":"sig-1"},{"type":"text","text":"Hello there"}],"stop_reason":"end_turn","usage":{"input_tokens":4,"output_tokens":3,"cache_read_input_tokens":1,"cache_creation_input_tokens":2}}}}
            {"custom_id":"req-2","result":{"type":"errored","error":{"type":"invalid_request_error","message":"bad context"}}}
            {"custom_id":"req-3","result":{"type":"canceled"}}
            {"custom_id":"req-4","result":{"type":"expired"}}
            """);
        var client = CreateClient(handler);

        var results = await client.GetResultsAsync(
            new ProviderBatchHandle("Claude", "msgbatch_1"),
            TestContext.Current.CancellationToken);

        handler.Requests.Single().Method.Should().Be(HttpMethod.Get);
        handler.Requests.Single().Path.Should()
            .Be("/v1/messages/batches/msgbatch_1/results");

        results.Should().HaveCount(4);

        var succeeded = results.Single(result => result.RequestId == "req-1");
        succeeded.State.Should().Be(BaizeBatchItemState.Succeeded);
        succeeded.Response!.Content.Should().Be("Hello there");
        succeeded.Response.Reasoning.Should().Be("let me think");
        succeeded.Response.ReasoningContinuation!.GetValue("signature")
            .Should().Be("sig-1");
        succeeded.Response.FinishReason.Should().Be("end_turn");
        succeeded.Response.Usage!.PromptTokens.Should().Be(4);
        succeeded.Response.Usage.TotalTokens.Should().Be(7);

        var errored = results.Single(result => result.RequestId == "req-2");
        errored.State.Should().Be(BaizeBatchItemState.Failed);
        errored.Error!.Message.Should().Be("bad context");
        errored.Error.FailureKind.Should().Be(LlmClientFailureKind.InvalidRequest);
        errored.Error.ProviderStatus.Should().Be("invalid_request_error");

        results.Single(result => result.RequestId == "req-3").State
            .Should().Be(BaizeBatchItemState.Cancelled);
        results.Single(result => result.RequestId == "req-4").State
            .Should().Be(BaizeBatchItemState.Expired);
    }

    [Fact]
    public async Task GetResultsAsync_MapsToolCallsAndStructuredOutput()
    {
        var handler = new ClaudeBatchRecordingHandler(
            """
            {"custom_id":"req-1","result":{"type":"succeeded","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{"city":"Paris"}},{"type":"text","text":"done"}]}}}
            {"custom_id":"req-2","result":{"type":"succeeded","message":{"content":[{"type":"tool_use","id":"toolu_2","name":"structured_output","input":{"result":"ok"}}]}}}
            """);
        var client = CreateClient(handler);

        var results = await client.GetResultsAsync(
            new ProviderBatchHandle("Claude", "msgbatch_1"),
            TestContext.Current.CancellationToken);

        var withTool = results.Single(result => result.RequestId == "req-1");
        withTool.Response!.ToolCalls.Should().HaveCount(1);
        withTool.Response.ToolCalls![0].Id.Should().Be("toolu_1");
        withTool.Response.ToolCalls[0].Name.Should().Be("get_weather");
        withTool.Response.ToolCalls[0].ArgumentsJson.Should().Be("""{"city":"Paris"}""");
        withTool.Response.Content.Should().Be("done");

        var structured = results.Single(result => result.RequestId == "req-2");
        structured.Response!.Content.Should().Be("""{"result":"ok"}""");
        structured.Response.ToolCalls.Should().BeNull();
    }

    [Fact]
    public async Task GetResultsAsync_ThrowsOnMalformedResultLine()
    {
        var handler = new ClaudeBatchRecordingHandler(
            "this is not json");
        var client = CreateClient(handler);

        var action = async () =>
            await client.GetResultsAsync(
                new ProviderBatchHandle("Claude", "msgbatch_1"),
                TestContext.Current.CancellationToken);

        await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage("*Failed to parse Anthropic batch result line*");
    }

    [Fact]
    public async Task GetResultsAsync_RejectsMissingCorrelationId()
    {
        var handler = new ClaudeBatchRecordingHandler(
            """{"result":{"type":"errored","error":{"type":"invalid_request_error","message":"bad"}}}""");
        var client = CreateClient(handler);

        var action = async () => await client.GetResultsAsync(
            new ProviderBatchHandle("Claude", "msgbatch_1"),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<LlmClientException>()
            .WithMessage("*has no custom_id*");
    }

    [Fact]
    public async Task CancelAsync_PostsToCancelEndpoint()
    {
        var handler = new ClaudeBatchRecordingHandler(
            """{"id":"msgbatch_1","processing_status":"canceled"}""");
        var client = CreateClient(handler);

        await client.CancelAsync(
            new ProviderBatchHandle("Claude", "msgbatch_1"),
            TestContext.Current.CancellationToken);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be("/v1/messages/batches/msgbatch_1/cancel");
    }

    [Fact]
    public async Task CancelAsync_ThrowsWhenCancellationNotAdvertised()
    {
        var handler = new ClaudeBatchRecordingHandler();
        var client = CreateClient(
            handler,
            DefaultCapabilities with
            {
                Batch = BatchCapabilities.NativeBatch | BatchCapabilities.Polling
            });

        var action = async () =>
            await client.CancelAsync(
                new ProviderBatchHandle("Claude", "msgbatch_1"),
                TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void Provider_CreateBatchClient_ReturnsConfiguredBatchClient()
    {
        var provider = new ClaudeClientProvider();

        var context = new LlmClientProviderContext(
            Model: "claude-test",
            HttpClientFactory: new TestHttpClientFactory(
                new HttpClient(new ClaudeBatchRecordingHandler())),
            ApiKey: "secret",
            BaseUrl: "https://claude.test",
            Capabilities: provider.DefaultCapabilities,
            Settings: new Dictionary<string, string>());

        var batchClient = provider.CreateBatchClient(context);

        batchClient.Should().NotBeNull();
        batchClient!.ProviderId.Should().Be("Claude");
        batchClient.Capabilities.Should().Be(
            BatchCapabilities.NativeBatch |
            BatchCapabilities.Polling |
            BatchCapabilities.Cancellation);
    }

    [Fact]
    public void Provider_CreateBatchClient_ReturnsNullWhenNativeBatchUnavailable()
    {
        var provider = new ClaudeClientProvider();

        var context = new LlmClientProviderContext(
            Model: "claude-test",
            HttpClientFactory: new TestHttpClientFactory(
                new HttpClient(new ClaudeBatchRecordingHandler())),
            ApiKey: "secret",
            BaseUrl: "https://claude.test",
            Capabilities: provider.DefaultCapabilities with { Batch = BatchCapabilities.None },
            Settings: new Dictionary<string, string>());

        provider.CreateBatchClient(context).Should().BeNull();
    }

    private static LlmEndpointCapabilities DefaultCapabilities =>
        new()
        {
            NativeToolCalling = true,
            ParallelToolCalls = true,
            NativeStructuredOutput = false,
            StructuredOutputViaTool = true,
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

    private static ClaudeBatchClient CreateClient(
        ClaudeBatchRecordingHandler handler,
        LlmEndpointCapabilities? capabilities = null) =>
        new(
            new TestHttpClientFactory(
                new HttpClient(handler)),
            model: "claude-test",
            apiKey: "secret",
            baseUrl: "https://claude.test",
            capabilities: capabilities ?? DefaultCapabilities,
            thinkingStyle: ClaudeThinkingStyle.Adaptive);

    private sealed class TestHttpClientFactory(
        HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name) =>
            client;
    }

    private sealed class ClaudeBatchRecordingHandler(
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
            IReadOnlyDictionary<string, string> Headers);

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
                header => string.Join(",", header.Value),
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
