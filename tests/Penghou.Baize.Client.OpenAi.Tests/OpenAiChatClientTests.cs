using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.OpenAi.Tests;

public sealed class OpenAiChatClientTests
{
    [Theory]
    [InlineData(OpenAiDialect.DeepSeek, "deepseek-chat", LlmThinkingMode.Enabled, "enabled", true)]
    [InlineData(OpenAiDialect.DeepSeek, "deepseek-chat", LlmThinkingMode.Disabled, "disabled", false)]
    [InlineData(OpenAiDialect.Standard, "gpt-4o-mini", LlmThinkingMode.Enabled, null, true)]
    [InlineData(OpenAiDialect.Standard, "gpt-4o-mini", LlmThinkingMode.ProviderDefault, null, false)]
    [InlineData(OpenAiDialect.DeepSeek, "deepseek-chat", LlmThinkingMode.ProviderDefault, null, false)]
    public async Task StreamAsync_MapsThinkingModeAndToggle(
        OpenAiDialect dialect,
        string model,
        LlmThinkingMode mode,
        string? expectedToggle,
        bool expectReasoningEffort)
    {
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":2,"total_tokens":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            model,
            dialect: dialect);
        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: mode,
                    effort: LlmThinkingEffort.Medium));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        var root = requestDocument.RootElement;

        root.TryGetProperty(
                "reasoning_effort",
                out var reasoningEffort)
            .Should()
            .Be(expectReasoningEffort);
        if (expectReasoningEffort)
        {
            reasoningEffort.GetString()
                .Should().Be("medium");
        }

        root.TryGetProperty(
                "thinking",
                out var thinking)
            .Should()
            .Be(expectedToggle is not null);
        if (expectedToggle is not null)
        {
            thinking.GetProperty("type")
                .GetString()
                .Should().Be(expectedToggle);
        }
    }

    [Fact]
    public async Task StreamAsync_DoesNotInferDialectFromModelName()
    {
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":null}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "deepseek-chat",
            dialect: OpenAiDialect.Standard);
        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.Medium));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        var root = requestDocument.RootElement;

        root.TryGetProperty("thinking", out _)
            .Should().BeFalse();
        root.GetProperty("reasoning_effort")
            .GetString()
            .Should().Be("medium");
    }

    [Fact]
    public async Task StreamAsync_ThrowsForDisabledThinkingOnStandardDialect()
    {
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":null}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gpt-4o-mini",
            dialect: OpenAiDialect.Standard);
        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Disabled));

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current.CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support disabling extended thinking*");
    }

    [Fact]
    public async Task StreamAsync_ThrowsForToolsWhenUnsupported()
    {
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gpt-4o-mini",
            DefaultCapabilities with { NativeToolCalling = false });
        var request = new LlmRequest(
            [new LlmMessage("user", "Use tools")],
            tools:
            [
                new LlmTool(
                    "get_weather",
                    "Gets the weather",
                    """{"type":"object"}""")
            ]);

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current.CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support native tool calling*");
    }

    [Fact]
    public async Task StreamAsync_ThrowsForStructuredOutputWhenUnsupported()
    {
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gpt-4o-mini",
            DefaultCapabilities with
            {
                NativeStructuredOutput = false,
                StructuredOutputViaTool = false
            });
        var request = new LlmRequest(
            [new LlmMessage("user", "Return JSON")],
            responseFormat:
                LlmResponseFormat.JsonSchema("""{"type":"object"}"""));

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current.CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support structured output*");
    }

    [Fact]
    public async Task StreamAsync_MapsSchemaLessJsonResponseFormat()
    {
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"{}"},"finish_reason":"stop"}]}

            data: [DONE]

            """);
        var client = CreateClient(handler, "gpt-4o-mini");

        await CollectAsync(client.StreamAsync(
            new LlmRequest(
                [new LlmMessage("user", "Return JSON")],
                responseFormat: LlmResponseFormat.Json()),
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        document.RootElement.GetProperty("response_format")
            .GetProperty("type").GetString().Should().Be("json_object");
    }

    [Fact]
    public async Task StreamAsync_DeepSeekDialectEnablesThinkingOnConservativeDefaults()
    {
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":null}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "deepseek-chat",
            ConservativeCapabilities,
            dialect: OpenAiDialect.DeepSeek);
        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.Medium));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        var root = requestDocument.RootElement;

        root.GetProperty("thinking")
            .GetProperty("type")
            .GetString()
            .Should().Be("enabled");
        root.GetProperty("reasoning_effort")
            .GetString()
            .Should().Be("medium");
    }

    [Fact]
    public void Constructor_DeepSeekDialectDoesNotInferThinkingWithoutBoost()
    {
        var client = CreateClient(
            new RecordingHandler("data: [DONE]"),
            "gpt-4o-mini",
            ConservativeCapabilities,
            dialect: OpenAiDialect.Standard);

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    new LlmRequest(
                        [new LlmMessage("user", "Reason")],
                        thinkingConfig:
                            new LlmThinkingConfig(
                                mode: LlmThinkingMode.Enabled)),
                    TestContext.Current.CancellationToken));

        action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support extended thinking*");
    }

    [Fact]
    public void StreamAsync_ThrowsForUnsupportedThinkingEffort()
    {
        var client = CreateClient(
            new RecordingHandler("data: [DONE]"),
            "gpt-4o-mini",
            DefaultCapabilities with
            {
                SupportedThinkingEfforts =
                    new HashSet<LlmThinkingEffort>
                    {
                        LlmThinkingEffort.Low,
                        LlmThinkingEffort.Medium,
                        LlmThinkingEffort.High
                    }
            });

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    new LlmRequest(
                        [new LlmMessage("user", "Reason hard")],
                        thinkingConfig:
                            new LlmThinkingConfig(
                                mode: LlmThinkingMode.Enabled,
                                effort: LlmThinkingEffort.Max)),
                    TestContext.Current.CancellationToken));

        action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support thinking effort 'Max'*");
    }

    [Fact]
    public void StreamAsync_RejectsMaxEffortWhenAdvertisedInsteadOfCapping()
    {
        // The endpoint advertises Max, so base validation passes; the adapter
        // must still reject it because the wire has no "max" reasoning effort.
        var client = CreateClient(
            new RecordingHandler("data: [DONE]"),
            "gpt-4o-mini",
            DefaultCapabilities);

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    new LlmRequest(
                        [new LlmMessage("user", "Reason hard")],
                        thinkingConfig:
                            new LlmThinkingConfig(
                                mode: LlmThinkingMode.Enabled,
                                effort: LlmThinkingEffort.Max)),
                    TestContext.Current.CancellationToken));

        action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*would be silently capped to 'high'*");
    }

    [Fact]
    public async Task StreamAsync_RejectsStreamWithoutTerminal()
    {
        // A stream that emits partial content but never reaches OpenAI's [DONE]
        // sentinel (for example the connection dropped mid-response) is
        // truncated and must be surfaced as an availability failure so the
        // router can fail over, rather than accepted as a complete answer.
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"partial"},"finish_reason":null}]}
            """);
        var client = CreateClient(
            handler,
            "gpt-4o-mini");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    new LlmRequest([new LlmMessage("user", "Hello")]),
                    TestContext.Current.CancellationToken));

        var exception = await action.Should()
            .ThrowAsync<LlmClientException>();
        exception.WithMessage("*ended without a terminal chunk*");
        exception.Which.FailureKind
            .Should().Be(LlmClientFailureKind.Availability);
    }

    [Theory]
    [InlineData(OpenAiDialect.Standard, false)]
    [InlineData(OpenAiDialect.DeepSeek, true)]
    public async Task StreamAsync_ReplaysReasoningOnlyForDeepSeekDialect(
        OpenAiDialect dialect,
        bool expectsLocalReasoning)
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var client = CreateClient(
            handler,
            dialect == OpenAiDialect.DeepSeek ? "deepseek-chat" : "gpt-4o-mini",
            dialect: dialect);
        var request = new LlmRequest(
        [
            new LlmMessage(
                "assistant",
            [
                new LlmReasoningContent("local"),
                new LlmReasoningContent("foreign")
                {
                    Continuation = new LlmProviderContinuation(
                        "Claude",
                        new Dictionary<string, string> { ["signature"] = "sig" })
                },
                new LlmTextContent("answer")
            ])
        ]);

        await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var message = document.RootElement.GetProperty("messages")[0];

        if (expectsLocalReasoning)
        {
            message.GetProperty("reasoning_content").GetString()
                .Should().Be("local");
        }
        else
        {
            message.TryGetProperty("reasoning_content", out _)
                .Should().BeFalse();
        }
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
            SupportedThinkingEfforts =
                new HashSet<LlmThinkingEffort>
                {
                    LlmThinkingEffort.Low,
                    LlmThinkingEffort.Medium,
                    LlmThinkingEffort.High,
                    LlmThinkingEffort.Max
                }
        };

    private static LlmEndpointCapabilities ConservativeCapabilities =>
        new()
        {
            NativeToolCalling = true,
            ParallelToolCalls = false,
            NativeStructuredOutput = false,
            StructuredOutputViaTool = false,
            Thinking = false,
            ThinkingDisable = false,
            StreamingToolCallArguments = true,
            SupportedThinkingEfforts =
                new HashSet<LlmThinkingEffort>
                {
                    LlmThinkingEffort.Low,
                    LlmThinkingEffort.Medium,
                    LlmThinkingEffort.High
                }
        };

    private static async Task<
        IReadOnlyList<LlmStreamEvent>>
        CollectAsync(
            IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var events =
            new List<LlmStreamEvent>();

        await foreach (var item in stream)
            events.Add(item);

        return events;
    }

    private sealed class TestHttpClientFactory(
        HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name) =>
            client;
    }

    private sealed class RecordingHandler(
        string responseBody)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<
            HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody =
                request.Content is null
                    ? null
                    : await request.Content
                        .ReadAsStringAsync(
                            cancellationToken);

            return new HttpResponseMessage(
                HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "text/event-stream")
            };
        }
    }

    private static OpenAiChatClient CreateClient(
        RecordingHandler handler,
        string model,
        LlmEndpointCapabilities? capabilities = null,
        OpenAiDialect dialect = OpenAiDialect.Standard) =>
        new(
            model,
            new TestHttpClientFactory(
                new HttpClient(handler)),
            apiKey: "secret",
            baseUrl: "https://openai.test/v1",
            capabilities: capabilities ?? DefaultCapabilities,
            dialect: dialect);
}
