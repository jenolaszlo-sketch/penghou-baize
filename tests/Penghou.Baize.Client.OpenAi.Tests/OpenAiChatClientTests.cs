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
    [Fact]
    public async Task StreamAsync_PreservesLeadingWhitespaceAndTwentyCharacterTail()
    {
        const string expected = "\nhead12345678901234567890";
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","choices":[{"index":0,"delta":{"content":"\nhead"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","choices":[{"index":0,"delta":{"content":"12345678901234567890"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """);
        var client = CreateClient(handler, "gpt-test");

        var response = await client.StreamAsync(
                new LlmRequest([new LlmMessage("user", "Reply")]),
                TestContext.Current.CancellationToken)
            .CollectAsync(cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be(expected);
        response.Content[^20..].Should().Be("12345678901234567890");
    }

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
    public async Task StreamAsync_DeepSeekJsonModeAddsRequiredJsonInstruction()
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var client = CreateClient(
            handler,
            "deepseek-chat",
            ConservativeCapabilities,
            dialect: OpenAiDialect.DeepSeek);

        await CollectAsync(client.StreamAsync(
            new LlmRequest(
                [new LlmMessage("user", "Return the result")],
                responseFormat: LlmResponseFormat.Json()),
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        root.GetProperty("messages")[0].GetProperty("role")
            .GetString().Should().Be("system");
        root.GetProperty("messages")[0].GetProperty("content")
            .GetString().Should().Contain("valid JSON");
        root.GetProperty("response_format").GetProperty("type")
            .GetString().Should().Be("json_object");
    }

    [Fact]
    public async Task StreamAsync_DeepSeekSchemaUsesForcedSyntheticToolAndReturnsContent()
    {
        var handler = new RecordingHandler(
            """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-1","function":{"name":"structured_output","arguments":"{\"answer\":"}}]},"finish_reason":null}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"ok\"}"}}]},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "deepseek-chat",
            ConservativeCapabilities,
            dialect: OpenAiDialect.DeepSeek);
        var events = await CollectAsync(client.StreamAsync(
            new LlmRequest(
                [new LlmMessage("user", "Return the answer")],
                responseFormat: LlmResponseFormat.JsonSchema(
                    """{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"]}""")),
            TestContext.Current.CancellationToken));

        string.Concat(events.Select(item => item.Delta))
            .Should().Be("{\"answer\":\"ok\"}");
        events.Should().OnlyContain(item => item.ToolCallDelta == null);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        root.TryGetProperty("response_format", out _).Should().BeFalse();
        root.GetProperty("tools")[0].GetProperty("function")
            .GetProperty("name").GetString().Should().Be("structured_output");
        root.GetProperty("tool_choice").GetProperty("function")
            .GetProperty("name").GetString().Should().Be("structured_output");
        root.GetProperty("thinking").GetProperty("type")
            .GetString().Should().Be("disabled");
    }

    [Fact]
    public async Task StreamAsync_RejectsExplicitThinkingWithDeepSeekSchemaFallback()
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var client = CreateClient(
            handler,
            "deepseek-chat",
            ConservativeCapabilities,
            dialect: OpenAiDialect.DeepSeek);
        var request = new LlmRequest(
            [new LlmMessage("user", "Return the answer")],
            responseFormat: LlmResponseFormat.JsonSchema(
                """{"type":"object"}"""),
            thinkingConfig: new LlmThinkingConfig(LlmThinkingMode.Enabled));

        var action = async () => await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        await action.Should().ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*cannot be combined with explicit thinking*");
        handler.RequestBody.Should().BeNull();
    }

    [Fact]
    public async Task StreamAsync_MapsStrictToolOnlyWhenExplicitlySupported()
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var client = CreateClient(
            handler,
            "deepseek-chat",
            ConservativeCapabilities with { StrictToolArguments = true },
            dialect: OpenAiDialect.DeepSeek);
        var tool = new LlmTool(
            "lookup",
            "Looks up a value",
            """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}""",
            Strict: true);

        await CollectAsync(client.StreamAsync(
            new LlmRequest(
                [new LlmMessage("user", "Look it up")],
                tools: [tool]),
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        document.RootElement.GetProperty("tools")[0]
            .GetProperty("function").GetProperty("strict")
            .GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task StreamAsync_RejectsStrictToolWhenCapabilityIsMissing()
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var client = CreateClient(handler, "gpt-test", ConservativeCapabilities);
        var request = new LlmRequest(
            [new LlmMessage("user", "Look it up")],
            tools:
            [
                new LlmTool(
                    "lookup",
                    "Looks up a value",
                    """{"type":"object"}""",
                    Strict: true)
            ]);

        var action = async () => await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        await action.Should().ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support strict tool arguments*");
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

    [Fact]
    public async Task StreamAsync_MapsToolsStructuredSchemaAndOmitsApplicationMetadata()
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var capabilities = DefaultCapabilities with
        {
            ToolsWithStructuredOutput = true
        };
        var client = CreateClient(handler, "gpt-4o-mini", capabilities);
        var request = new LlmRequest(
            [new LlmMessage("user", "Look up the weather and return JSON")],
            temperature: 0.1,
            maxTokens: 200,
            tools:
            [
                new LlmTool(
                    "get_weather",
                    "Gets weather",
                    """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""")
            ],
            responseFormat: LlmResponseFormat.JsonSchema(
                """{"type":"object","properties":{"summary":{"type":"string"}},"required":["summary"]}"""),
            metadata: new Dictionary<string, object?>
            {
                ["acme.tenant-id"] = "private-tenant"
            });

        await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        root.GetProperty("stream").GetBoolean().Should().BeTrue();
        root.GetProperty("stream_options").GetProperty("include_usage")
            .GetBoolean().Should().BeTrue();
        root.GetProperty("temperature").GetDouble().Should().Be(0.1);
        root.GetProperty("max_tokens").GetInt32().Should().Be(200);
        root.GetProperty("tools")[0].GetProperty("function")
            .GetProperty("parameters").GetProperty("required")[0]
            .GetString().Should().Be("city");
        var responseFormat = root.GetProperty("response_format");
        responseFormat.GetProperty("type").GetString().Should().Be("json_schema");
        responseFormat.GetProperty("json_schema").GetProperty("strict")
            .GetBoolean().Should().BeTrue();
        root.TryGetProperty("metadata", out _).Should().BeFalse();
        handler.RequestBody.Should().NotContain("private-tenant");
    }

    [Fact]
    public async Task StreamAsync_ReplaysToolCallsAndMultipleToolResults()
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var client = CreateClient(handler, "gpt-4o-mini");
        var request = new LlmRequest(
        [
            LlmMessage.Assistant(
                [new LlmToolCall("call-1", "lookup", "{\"id\":1}")],
                "checking"),
            LlmMessage.ToolResults(
            [
                new LlmToolResult("call-1", "lookup", "first"),
                new LlmToolResult("call-2", "lookup", "second")
            ])
        ]);

        await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var messages = document.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(3);
        messages[0].GetProperty("content").GetString().Should().Be("checking");
        messages[0].GetProperty("tool_calls")[0].GetProperty("function")
            .GetProperty("arguments").GetString().Should().Be("{\"id\":1}");
        messages[1].GetProperty("tool_call_id").GetString().Should().Be("call-1");
        messages[2].GetProperty("content").GetString().Should().Be("second");
    }

    [Fact]
    public async Task StreamAsync_MapsSupportedMultimodalTransports()
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var capabilities = DefaultCapabilities with
        {
            ContentTypes = new HashSet<LlmContentType>
            {
                LlmContentType.Text,
                LlmContentType.Image,
                LlmContentType.Audio,
                LlmContentType.File
            },
            ContentTransports = new Dictionary<LlmContentType, LlmContentTransport>
            {
                [LlmContentType.Image] =
                    LlmContentTransport.Uri | LlmContentTransport.InlineData,
                [LlmContentType.Audio] = LlmContentTransport.InlineData,
                [LlmContentType.File] =
                    LlmContentTransport.InlineData | LlmContentTransport.ProviderFile
            }
        };
        var client = CreateClient(handler, "gpt-4o-mini", capabilities);
        var request = new LlmRequest(
        [
            new LlmMessage("user",
            [
                new LlmTextContent("inspect"),
                new LlmImageContent(
                    "image/png",
                    new LlmUriSource(new Uri("https://example.test/image.png"))),
                new LlmImageContent(
                    "image/png",
                    new LlmInlineDataSource(new byte[] { 1, 2 })),
                new LlmAudioContent(
                    "audio/x-wav",
                    new LlmInlineDataSource(new byte[] { 3, 4 })),
                new LlmFileContent(
                    "application/pdf",
                    new LlmInlineDataSource(new byte[] { 5 }),
                    "inline.pdf"),
                new LlmFileContent(
                    "application/pdf",
                    new LlmProviderFileSource(new LlmProviderKey("OpenAi"), "file-1"),
                    "uploaded.pdf")
            ])
        ]);

        await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var content = document.RootElement.GetProperty("messages")[0]
            .GetProperty("content");
        content.GetArrayLength().Should().Be(6);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[1].GetProperty("image_url").GetProperty("url")
            .GetString().Should().Be("https://example.test/image.png");
        content[2].GetProperty("image_url").GetProperty("url")
            .GetString().Should().StartWith("data:image/png;base64,");
        content[3].GetProperty("input_audio").GetProperty("format")
            .GetString().Should().Be("wav");
        content[4].GetProperty("file").GetProperty("file_data")
            .GetString().Should().StartWith("data:application/pdf;base64,");
        content[5].GetProperty("file").GetProperty("file_id")
            .GetString().Should().Be("file-1");
    }

    [Fact]
    public async Task StreamAsync_RejectsWireUnsupportedMediaAfterCapabilityValidation()
    {
        var handler = new RecordingHandler("data: [DONE]\n\n");
        var capabilities = DefaultCapabilities with
        {
            ContentTypes = new HashSet<LlmContentType>
            {
                LlmContentType.Text,
                LlmContentType.Audio,
                LlmContentType.File
            },
            ContentTransports = new Dictionary<LlmContentType, LlmContentTransport>
            {
                [LlmContentType.Audio] =
                    LlmContentTransport.InlineData | LlmContentTransport.Uri,
                [LlmContentType.File] = LlmContentTransport.ProviderFile
            }
        };
        var client = CreateClient(handler, "gpt-4o-mini", capabilities);

        var badAudio = async () => await CollectAsync(client.StreamAsync(
            new LlmRequest(
            [
                new LlmMessage("user",
                [
                    new LlmAudioContent(
                        "audio/ogg",
                        new LlmInlineDataSource(new byte[] { 1 }))
                ])
            ]),
            TestContext.Current.CancellationToken));
        var foreignFile = async () => await CollectAsync(client.StreamAsync(
            new LlmRequest(
            [
                new LlmMessage("user",
                [
                    new LlmFileContent(
                        "application/pdf",
                        new LlmProviderFileSource(new LlmProviderKey("Gemini"), "file-1"))
                ])
            ]),
            TestContext.Current.CancellationToken));

        await badAudio.Should().ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support inline audio media type 'audio/ogg'*");
        await foreignFile.Should().ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support file source*");
        handler.RequestBody.Should().BeNull();
    }

    [Fact]
    public async Task StreamAsync_MapsReasoningFragmentedToolCallUsageAndFinish()
    {
        var handler = new RecordingHandler(
            """
            data: {"id":"chatcmpl-test","model":"actual-model","choices":[{"index":0,"delta":{"reasoning_content":"thinking","tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"lookup","arguments":"{\"city\":"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","model":"actual-model","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"Paris\"}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":8,"completion_tokens":3,"total_tokens":11}}

            data: [DONE]

            """);
        var client = CreateClient(handler, "gpt-4o-mini");

        var events = await CollectAsync(client.StreamAsync(
            new LlmRequest([new LlmMessage("user", "weather")]),
            TestContext.Current.CancellationToken));

        events.Should().Contain(item => item.ReasoningContent == "thinking");
        events.Where(item => item.ToolCallDelta is not null).Should().HaveCount(2);
        events.Should().Contain(item => item.FinishReason == "tool_calls");
        events.Any(item => item.Usage?.TotalTokens == 11).Should().BeTrue();
        events.Where(item => item.Diagnostics is not null)
            .Select(item => item.Diagnostics!.NativeToolCallCount)
            .Should().ContainInOrder(1, 1, 1);
        var terminalDiagnostics = events.Last().Diagnostics;
        terminalDiagnostics.Should().NotBeNull();
        terminalDiagnostics!.Done.Should().BeTrue();
        terminalDiagnostics.ResponseId.Should().Be("chatcmpl-test");
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
