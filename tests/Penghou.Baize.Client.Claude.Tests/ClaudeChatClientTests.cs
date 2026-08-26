using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize;
using Penghou.Baize.Claude;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Claude.Tests;

public sealed class ClaudeChatClientTests
{
    [Fact]
    public async Task StreamAsync_PreservesLeadingWhitespaceAndTwentyCharacterTail()
    {
        const string expected = "\nhead12345678901234567890";
        var handler = new RecordingHandler(
            """
            event: message_start
            data: {"type":"message_start","message":{"model":"claude-test","usage":{"input_tokens":1,"output_tokens":0}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"\nhead"}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"12345678901234567890"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":2}}

            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(handler, "claude-test");

        var response = await client.StreamAsync(
                CreateRequest(),
                TestContext.Current.CancellationToken)
            .CollectAsync(cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be(expected);
        response.Content[^20..].Should().Be("12345678901234567890");
    }

    [Fact]
    public async Task StreamAsync_MapsTextToolCallUsageAndRequest()
    {
        var handler = new RecordingHandler(
            """
            event: message_start
            data: {"type":"message_start","message":{"model":"claude-test","usage":{"input_tokens":12,"output_tokens":1,"cache_read_input_tokens":4,"cache_creation_input_tokens":2}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"working"}}

            event: content_block_start
            data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_123","name":"emit_files","input":{}}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"files\":["}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"]}"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":7}}

            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");
        var request = CreateRequest();

        var events = await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        events.Single(item => item.Delta is not null)
            .Delta.Should().Be("working");

        var toolDeltas = events
            .Where(item => item.ToolCallDelta is not null)
            .Select(item => item.ToolCallDelta!)
            .ToList();
        toolDeltas.Should().HaveCount(3);
        toolDeltas[0].Index.Should().Be(1);
        toolDeltas[0].Id.Should().Be("toolu_123");
        toolDeltas[0].Name.Should().Be("emit_files");
        string.Concat(
                toolDeltas.Select(item =>
                    item.ArgumentsJsonFragment))
            .Should().Be("""{"files":[]}""");

        var finalEvent = events.Last();
        finalEvent.FinishReason.Should().Be("tool_use");
        finalEvent.Usage.Should().Be(
            new LlmUsage(
                PromptTokens: 12,
                CompletionTokens: 7,
                TotalTokens: 19,
                PromptCacheHitTokens: 4,
                PromptCacheMissTokens: 2));

        handler.ClientName.Should().Be("llm");
        handler.RequestUri.Should().Be(
            new Uri("https://claude.test/v1/messages"));
        handler.ApiKey.Should().Be("secret");

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        var root = requestDocument.RootElement;
        root.GetProperty("model")
            .GetString()
            .Should().Be("claude-test");
        root.GetProperty("stream")
            .GetBoolean()
            .Should().BeTrue();
        root.GetProperty("system")
            .GetString()
            .Should().Contain("First system");
        root.GetProperty("system")
            .GetString()
            .Should().Contain("Second system");
        root.GetProperty("messages")
            .GetArrayLength()
            .Should().Be(1);
        root.GetProperty("tools")[0]
            .GetProperty("input_schema")
            .GetProperty("type")
            .GetString()
            .Should().Be("object");
    }

    [Fact]
    public async Task StreamAsync_ThrowsForToolsWhenUnsupported()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test",
            capabilities: DefaultCapabilities with { NativeToolCalling = false });

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    CreateRequest(),
                    TestContext.Current.CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support native tool calling*");
    }

    [Fact]
    public async Task StreamAsync_ThrowsWhenCombiningToolsAndStructuredOutput()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

        var request = new LlmRequest(
            [new LlmMessage("user", "Generate files")],
            tools:
            [
                new LlmTool(
                    "emit_files",
                    "Emits files",
                    """{"type":"object"}""")
            ],
            responseFormat:
                LlmResponseFormat.JsonSchema("""{"type":"object"}"""));

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current.CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support combining tools with structured output*");
    }

    [Fact]
    public async Task StreamAsync_OmitsOutputConfigForProviderDefaultThinking()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.ProviderDefault));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        requestDocument.RootElement
            .TryGetProperty("output_config", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_ThrowsForDisabledThinkingWhenUnsupported()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

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
    public async Task StreamAsync_EmitsAdaptiveThinkingBlockAndEffortForEnabledThinking()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

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
            .Should().Be("adaptive");
        root.GetProperty("output_config")
            .GetProperty("effort")
            .GetString()
            .Should().Be("medium");
    }

    [Fact]
    public async Task StreamAsync_EmitsManualThinkingBudgetForEnabledThinking()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test",
            thinkingStyle: ClaudeThinkingStyle.Manual,
            capabilities: DefaultCapabilities with
            {
                ThinkingBudget = 2048
            });

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
        root.GetProperty("thinking")
            .GetProperty("budget_tokens")
            .GetInt32()
            .Should().Be(2048);
        root.TryGetProperty("output_config", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_DerivesManualThinkingBudgetFromEffort()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test",
            thinkingStyle: ClaudeThinkingStyle.Manual);

        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.High));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        requestDocument.RootElement
            .GetProperty("thinking")
            .GetProperty("budget_tokens")
            .GetInt32()
            .Should().Be(8192);
    }

    [Fact]
    public async Task StreamAsync_ManualThinkingRequiresBudgetWhenNoEffortGiven()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test",
            thinkingStyle: ClaudeThinkingStyle.Manual);

        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.None));

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current.CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*requires a token budget*");
    }

    [Fact]
    public async Task StreamAsync_ManualThinkingUsesExplicitBudgetWhenNoEffortGiven()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test",
            thinkingStyle: ClaudeThinkingStyle.Manual,
            capabilities: DefaultCapabilities with
            {
                ThinkingBudget = 4096
            });

        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.None));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        var thinking =
            requestDocument.RootElement.GetProperty("thinking");
        thinking.GetProperty("type").GetString().Should().Be("enabled");
        thinking.GetProperty("budget_tokens").GetInt32().Should().Be(4096);
    }

    [Fact]
    public async Task StreamAsync_EmitsDisabledThinkingBlockWhenAdvertised()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test",
            capabilities: DefaultCapabilities with
            {
                ThinkingDisable = true
            });

        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Disabled,
                    effort: LlmThinkingEffort.Medium));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        requestDocument.RootElement
            .GetProperty("thinking")
            .GetProperty("type")
            .GetString()
            .Should().Be("disabled");
    }

    [Fact]
    public async Task StreamAsync_RejectsMaxEffortInsteadOfCapping()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test",
            capabilities: DefaultCapabilities with
            {
                SupportedThinkingEfforts =
                    new HashSet<LlmThinkingEffort>
                    {
                        LlmThinkingEffort.Low,
                        LlmThinkingEffort.Medium,
                        LlmThinkingEffort.High,
                        LlmThinkingEffort.Max
                    }
            });

        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.Max));

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current.CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*would be silently capped to 'high'*");
    }

    [Fact]
    public async Task StreamAsync_ThrowsForErrorEventAfterSuccessfulHeaders()
    {
        var handler = new RecordingHandler(
            """
            event: error
            data: {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    CreateRequest(),
                    TestContext.Current.CancellationToken));

        var exception = await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage(
                "*overloaded_error*Overloaded*");
        exception.Which.FailureKind
            .Should().Be(LlmClientFailureKind.Availability);
        exception.Which.CanFallback.Should().BeTrue();
    }

    [Fact]
    public async Task StreamAsync_SurfacesThinkingDeltaAsReasoning()
    {
        var handler = new RecordingHandler(
            """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Let me reason about this."}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}

            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

        var events = await CollectAsync(
            client.StreamAsync(
                CreateRequest(),
                TestContext.Current.CancellationToken));

        events.Single(item =>
                item.ReasoningContent is not null)
            .ReasoningContent.Should()
            .Be("Let me reason about this.");
        events.Single(item => item.Delta is not null)
            .Delta.Should().Be("answer");
    }

    [Fact]
    public async Task StreamAsync_CapturesThinkingSignatureAsContinuation()
    {
        var handler = new RecordingHandler(
            """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Let me think about it."}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig_123"}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}

            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

        var events = await CollectAsync(
            client.StreamAsync(
                CreateRequest(),
                TestContext.Current.CancellationToken));

        events.Single(item =>
                item.ReasoningContent is not null)
            .ReasoningContent.Should()
            .Be("Let me think about it.");

        var signatureEvent = events.Single(item =>
            item.Continuation is not null &&
            item.ReasoningContent is null &&
            item.Delta is null);
        signatureEvent.Continuation!
            .Provider.Should().Be("Claude");
        signatureEvent.Continuation!
            .GetValue("signature")
            .Should().Be("sig_123");
    }

    [Fact]
    public async Task CompleteStreamingAsync_RetainsThinkingSignatureWithReasoning()
    {
        var handler = new RecordingHandler(
            """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Let me think about it."}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig_123"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}

            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");
        var router = new LlmRouter(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>>
                {
                    ["claude-native"] = () => client
                },
                new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
                {
                    [("claude-native", ApiStyle.Claude)] = () => client
                }),
            new Dictionary<
                ModelStrategy,
                IReadOnlyList<string>>());

        var response =
            await router.CompleteStreamingAsync(
                "claude-native",
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Reason")]
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        response.Reasoning.Should()
            .Be("Let me think about it.");
        response.ReasoningContinuation.Should().NotBeNull();
        response.ReasoningContinuation!
            .GetValue("signature").Should().Be("sig_123");
    }

    [Fact]
    public async Task CompleteStreamingAsync_PreservesSignatureOnlyAndRedactedThinking()
    {
        var handler = new RecordingHandler(
            """
            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig_empty"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: content_block_start
            data: {"type":"content_block_start","index":1,"content_block":{"type":"redacted_thinking","data":"opaque_data"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":1}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}

            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(handler, "claude-test");
        var router = CreateRouter(client);

        var response = await router.CompleteStreamingAsync(
            "claude-native",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Reason")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Parts.Should().HaveCount(2);
        var signatureOnly = response.Parts![0]
            .Should().BeOfType<LlmReasoningContent>().Subject;
        signatureOnly.Text.Should().BeEmpty();
        signatureOnly.Continuation!.GetValue("signature")
            .Should().Be("sig_empty");
        var redacted = response.Parts[1]
            .Should().BeOfType<LlmReasoningContent>().Subject;
        redacted.Text.Should().BeEmpty();
        redacted.Continuation!.GetValue("redactedThinkingData")
            .Should().Be("opaque_data");
    }

    [Fact]
    public async Task StreamAsync_ReplaysRedactedThinkingAndDropsForeignReasoning()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(handler, "claude-test");
        var request = new LlmRequest(
        [
            new LlmMessage(
                "assistant",
            [
                new LlmReasoningContent("foreign")
                {
                    Continuation = new LlmProviderContinuation(
                        "Gemini",
                        new Dictionary<string, string>
                        {
                            ["thoughtSignature"] = "gemini_sig"
                        })
                },
                new LlmReasoningContent(string.Empty)
                {
                    Continuation = new LlmProviderContinuation(
                        "Claude",
                        new Dictionary<string, string>
                        {
                            ["redactedThinkingData"] = "opaque_data"
                        })
                },
                new LlmToolCallContent(
                    new LlmToolCall("call_1", "lookup", "{}"))
            ])
        ]);

        await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var content = document.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content");
        content.GetArrayLength().Should().Be(2);
        content[0].GetProperty("type").GetString()
            .Should().Be("redacted_thinking");
        content[0].GetProperty("data").GetString()
            .Should().Be("opaque_data");
        content[1].GetProperty("type").GetString()
            .Should().Be("tool_use");
    }

    [Fact]
    public async Task StreamAsync_DeliversStructuredOutputAsContentNotToolCall()
    {
        var handler = new RecordingHandler(
            """
            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_so","name":"structured_output","input":{}}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"name\":\"me\""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":",\"role\":\"engineer\"}"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0,"content_block":{"type":"tool_use","id":"toolu_so","name":"structured_output"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":3}}

            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");
        var request = new LlmRequest(
            [new LlmMessage("user", "Return the schema shape")],
            responseFormat:
                LlmResponseFormat.JsonSchema(
                    """{"type":"object","properties":{"name":{"type":"string"}}}"""));

        var events = await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        events.Should().NotContain(
            item => item.ToolCallDelta != null);
        events.Single(item => item.Delta is not null)
            .Delta.Should()
            .Be("""{"name":"me","role":"engineer"}""");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StreamAsync_EmitsIncompleteStructuredOutputBeforeFailure(
        bool includesMessageStop)
    {
        var responseBody = """
            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_so","name":"structured_output","input":{}}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"name\":\"partial\""}}
            """;
        if (includesMessageStop)
        {
            responseBody += "\n\nevent: message_stop\n" +
                            "data: {\"type\":\"message_stop\"}\n\n";
        }

        var handler = new RecordingHandler(responseBody);
        var client = CreateClient(handler, "claude-test");
        var request = new LlmRequest(
            [new LlmMessage("user", "Return the schema shape")],
            responseFormat:
                LlmResponseFormat.JsonSchema(
                    """{"type":"object","properties":{"name":{"type":"string"}}}"""));
        var emitted = new List<LlmStreamEvent>();

        var action = async () =>
        {
            await foreach (var item in client.StreamAsync(
                               request,
                               TestContext.Current.CancellationToken))
            {
                emitted.Add(item);
            }
        };

        var exception = await action.Should().ThrowAsync<LlmClientException>();
        exception.Which.FailureKind.Should().Be(
            includesMessageStop
                ? LlmClientFailureKind.Protocol
                : LlmClientFailureKind.Availability);
        emitted.Single(item => item.Delta is not null)
            .Delta.Should().Be("{\"name\":\"partial\"");
    }

    [Fact]
    public async Task StreamAsync_ThrowsOnMalformedEvent()
    {
        var handler = new RecordingHandler(
            """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"partial"}}

            event: content_block_delta
            data: this is not json

            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

        var action = async () => await CollectAsync(
            client.StreamAsync(
                CreateRequest(),
                TestContext.Current.CancellationToken));

        await action.Should().ThrowAsync<LlmClientException>()
            .WithMessage("*Failed to parse Claude streaming event*");
    }

    [Fact]
    public async Task Router_UsesConfiguredClaudeProviderModelAndBaseUrl()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LlmRouting:Models:0:Name"] =
                            "claude-alias",
                        ["LlmRouting:Models:0:Endpoints:0:ApiStyle"] =
                            "Claude",
                        ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] =
                            "claude-provider-model",
                        ["LlmRouting:Models:0:Endpoints:0:BaseUrl"] =
                            "https://router.claude.test"
                    })
                .Build();
        var services = new ServiceCollection();
        services.AddHttpClient("llm")
            .ConfigurePrimaryHttpMessageHandler(
                () => handler);
        services.AddClaudeLlmProvider();
        services.AddLlmRouting(configuration);

        await using var provider =
            services.BuildServiceProvider();
        var models = provider.GetRequiredService<ILlmModelLookup>();

        await CollectAsync(
            models.GetClient("claude-alias")
                .StreamAsync(
                    new LlmRequest(
                        [new LlmMessage("user", "Hello")]),
                    TestContext.Current.CancellationToken));

        handler.RequestUri.Should().Be(
            new Uri(
                "https://router.claude.test/v1/messages"));
        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        requestDocument.RootElement
            .GetProperty("model")
            .GetString()
            .Should().Be("claude-provider-model");
    }

    [Fact]
    public async Task Router_UsesConfiguredClaudeThinkingStyle()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LlmRouting:Models:0:Name"] =
                            "manual-claude",
                        ["LlmRouting:Models:0:Endpoints:0:ApiStyle"] =
                            "Claude",
                        ["LlmRouting:Models:0:Endpoints:0:ThinkingStyle"] =
                            "Manual"
                    })
                .Build();
        var services = new ServiceCollection();
        services.AddHttpClient("llm")
            .ConfigurePrimaryHttpMessageHandler(
                () => handler);
        services.AddClaudeLlmProvider();
        services.AddLlmRouting(configuration);

        await using var provider =
            services.BuildServiceProvider();
        var models = provider.GetRequiredService<ILlmModelLookup>();

        await CollectAsync(
            models.GetClient("manual-claude")
                .StreamAsync(
                    new LlmRequest(
                        [new LlmMessage("user", "Reason")],
                        thinkingConfig:
                            new LlmThinkingConfig(
                                mode: LlmThinkingMode.Enabled,
                                effort: LlmThinkingEffort.Medium)),
                    TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        var thinking =
            requestDocument.RootElement
                .GetProperty("thinking");
        thinking.GetProperty("type")
            .GetString().Should().Be("enabled");
        thinking.GetProperty("budget_tokens")
            .GetInt32().Should().Be(4096);
        requestDocument.RootElement
            .TryGetProperty("output_config", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_EmitsRateLimitEventFromAnthropicHeaders()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """,
            responseHeaders:
                new Dictionary<string, string>
                {
                    ["anthropic-ratelimit-tokens-limit"] =
                        "100000",
                    ["anthropic-ratelimit-tokens-remaining"] =
                        "40000",
                    ["anthropic-ratelimit-tokens-reset"] =
                        "2026-08-08T12:34:56Z"
                });
        var client = CreateClient(
            handler,
            "claude-test");

        var events = await CollectAsync(
            client.StreamAsync(
                CreateRequest(),
                TestContext.Current.CancellationToken));

        var rateLimit = events.Last().RateLimit;
        rateLimit.Should().NotBeNull();
        rateLimit!.TokensLimit.Should().Be(100000);
        rateLimit.TokensRemaining.Should().Be(40000);
        rateLimit.TokensResetAt.Should().NotBeNull();
        rateLimit.RequestsRemaining.Should().BeNull();
    }

    [Fact]
    public async Task StreamAsync_ParsesOpenAiStyleRateLimitHeaders()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """,
            responseHeaders:
                new Dictionary<string, string>
                {
                    ["x-ratelimit-limit-requests"] = "60",
                    ["x-ratelimit-remaining-requests"] = "2",
                    ["x-ratelimit-reset-requests"] = "8s"
                });
        var client = CreateClient(
            handler,
            "claude-test");

        var events = await CollectAsync(
            client.StreamAsync(
                CreateRequest(),
                TestContext.Current.CancellationToken));

        var rateLimit = events.Last().RateLimit;
        rateLimit.Should().NotBeNull();
        rateLimit!.RequestsLimit.Should().Be(60);
        rateLimit.RequestsRemaining.Should().Be(2);
        rateLimit.RequestsResetAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddSeconds(8),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task StreamAsync_ThrowsWithRateLimitForRateLimitedResponse()
    {
        var handler = new RecordingHandler(
            """{"error":{"type":"rate_limit_error","message":"slow down"}}""",
            statusCode: HttpStatusCode.TooManyRequests,
            responseHeaders:
                new Dictionary<string, string>
                {
                    ["anthropic-ratelimit-tokens-remaining"] =
                        "0"
                },
            retryAfter: TimeSpan.FromSeconds(3));
        var client = CreateClient(
            handler,
            "claude-test");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    CreateRequest(),
                    TestContext.Current.CancellationToken));

        var exception = await action.Should()
            .ThrowAsync<LlmClientException>();
        exception.Which.StatusCode.Should().Be(429);
        exception.Which.RateLimit.Should().NotBeNull();
        exception.Which.RateLimit!.TokensRemaining
            .Should().Be(0);
        exception.Which.RateLimit.RetryAfter
            .Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task StreamAsync_ThrowsWithRetryAfterForErrorEvent()
    {
        var handler = new RecordingHandler(
            """
            event: error
            data: {"type":"error","error":{"type":"rate_limit_error","message":"slow down","retry_after":3}}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    CreateRequest(),
                    TestContext.Current.CancellationToken));

        var exception = await action.Should()
            .ThrowAsync<LlmClientException>();
        exception.Which.FailureKind
            .Should().Be(LlmClientFailureKind.RateLimit);
        exception.Which.CanFallback.Should().BeTrue();
        exception.Which.RateLimit.Should().NotBeNull();
        exception.Which.RateLimit!.RetryAfter
            .Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task StreamAsync_RoundTripsToolCallConversation()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(handler, "claude-test");
        var request = new LlmRequest(
            [
                LlmMessage.Assistant(
                    [new LlmToolCall("call_1", "get_weather", """{"city":"Paris"}""")],
                    text: "Let me check."),
                LlmMessage.ToolResults(
                    [new LlmToolResult("call_1", "get_weather", """{"temp":21}""")]),
                LlmMessage.Text("user", "Great.")
            ]);

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var document =
            JsonDocument.Parse(handler.RequestBody!);
        var messages = document.RootElement
            .GetProperty("messages");
        messages.GetArrayLength().Should().Be(3);

        var assistantContent =
            messages[0].GetProperty("content");
        assistantContent.GetArrayLength().Should().Be(2);
        assistantContent[0].GetProperty("type")
            .GetString().Should().Be("text");
        assistantContent[0].GetProperty("text")
            .GetString().Should().Be("Let me check.");
        assistantContent[1].GetProperty("type")
            .GetString().Should().Be("tool_use");
        assistantContent[1].GetProperty("id")
            .GetString().Should().Be("call_1");
        assistantContent[1].GetProperty("name")
            .GetString().Should().Be("get_weather");
        assistantContent[1].GetProperty("input")
            .GetProperty("city")
            .GetString().Should().Be("Paris");

        var toolResult =
            messages[1].GetProperty("content")[0];
        messages[1].GetProperty("role")
            .GetString().Should().Be("user");
        toolResult.GetProperty("type")
            .GetString().Should().Be("tool_result");
        toolResult.GetProperty("tool_use_id")
            .GetString().Should().Be("call_1");
        toolResult.GetProperty("content")
            .GetString().Should().Contain("temp");
        toolResult.GetProperty("is_error")
            .GetBoolean().Should().BeFalse();

        messages[2].GetProperty("role")
            .GetString().Should().Be("user");
    }

    [Fact]
    public async Task StreamAsync_ReplaysThinkingBlockWithSignatureBeforeToolUse()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(handler, "claude-test");
        var request = new LlmRequest(
            [
                new LlmMessage(
                    "assistant",
                    [
                        new LlmReasoningContent("Let me think about it.")
                        {
                            Continuation =
                                new LlmProviderContinuation(
                                    "Claude",
                                    new Dictionary<string, string>
                                    {
                                        ["signature"] = "sig_123"
                                    })
                        },
                        new LlmToolCallContent(
                            new LlmToolCall(
                                "call_1",
                                "get_weather",
                                """{"city":"Paris"}"""))
                    ]),
                LlmMessage.ToolResults(
                    [new LlmToolResult("call_1", "get_weather", """{"temp":21}""")])
            ]);

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var document =
            JsonDocument.Parse(handler.RequestBody!);
        var assistantContent = document.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content");
        assistantContent.GetArrayLength().Should().Be(2);

        var thinking = assistantContent[0];
        thinking.GetProperty("type")
            .GetString().Should().Be("thinking");
        thinking.GetProperty("thinking")
            .GetString().Should().Be("Let me think about it.");
        thinking.GetProperty("signature")
            .GetString().Should().Be("sig_123");

        assistantContent[1].GetProperty("type")
            .GetString().Should().Be("tool_use");
        assistantContent[1].GetProperty("id")
            .GetString().Should().Be("call_1");
    }

    [Fact]
    public async Task StreamAsync_SendsParallelToolResultsInOneUserMessage()
    {
        var handler = new RecordingHandler(
            """
            event: message_stop
            data: {"type":"message_stop"}

            """);
        var client = CreateClient(handler, "claude-test");
        var request = new LlmRequest(
            [
                new LlmMessage(
                    "assistant",
                    [
                        new LlmToolCallContent(
                            new LlmToolCall(
                                "call_1",
                                "get_weather",
                                """{"city":"Paris"}""")),
                        new LlmToolCallContent(
                            new LlmToolCall(
                                "call_2",
                                "get_time",
                                """{"tz":"UTC"}"""))
                    ]),
                LlmMessage.ToolResults(
                    [
                        new LlmToolResult("call_1", "get_weather", """{"temp":21}"""),
                        new LlmToolResult("call_2", "get_time", """{"time":"10:00"}""")
                    ])
            ]);

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var document =
            JsonDocument.Parse(handler.RequestBody!);
        var messages = document.RootElement
            .GetProperty("messages");
        var toolMessage = messages[1];
        toolMessage.GetProperty("role")
            .GetString().Should().Be("user");

        var results = toolMessage.GetProperty("content");
        results.GetArrayLength().Should().Be(2);
        results[0].GetProperty("type")
            .GetString().Should().Be("tool_result");
        results[0].GetProperty("tool_use_id")
            .GetString().Should().Be("call_1");
        results[1].GetProperty("type")
            .GetString().Should().Be("tool_result");
        results[1].GetProperty("tool_use_id")
            .GetString().Should().Be("call_2");
    }

    [Fact]
    public async Task StreamAsync_RejectsStreamWithoutMessageStop()
    {
        // A stream that emits deltas and a finish reason but never sends the
        // terminating message_stop event is truncated and must not be accepted
        // as a complete answer.
        var handler = new RecordingHandler(
            """
            event: message_start
            data: {"type":"message_start","message":{"usage":{"input_tokens":10,"output_tokens":0}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"partial"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":2}}

            """);
        var client = CreateClient(
            handler,
            "claude-test");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    CreateRequest(),
                    TestContext.Current.CancellationToken));

        var exception = await action.Should()
            .ThrowAsync<LlmClientException>();
        exception.WithMessage("*ended without a message_stop event*");
        exception.Which.FailureKind
            .Should().Be(LlmClientFailureKind.Availability);
    }

    private static LlmRequest CreateRequest() =>
        new(
            [
                new LlmMessage(
                    "system",
                    "First system"),
                new LlmMessage(
                    "system",
                    "Second system"),
                new LlmMessage(
                    "user",
                    "Generate files")
            ],
            temperature: 0.1,
            maxTokens: 2048,
            tools:
            [
                new LlmTool(
                    "emit_files",
                    "Emits files",
                    """
                    {
                      "type": "object",
                      "properties": {
                        "files": { "type": "array" }
                      },
                      "required": ["files"]
                    }
                    """)
            ]);

    private static ClaudeChatClient CreateClient(
        RecordingHandler handler,
        string model,
        ClaudeThinkingStyle? thinkingStyle = null,
        LlmEndpointCapabilities? capabilities = null) =>
        new(
            new TestHttpClientFactory(
                new HttpClient(handler),
                handler),
            model,
            apiKey: "secret",
            baseUrl: "https://claude.test",
            capabilities: capabilities ?? DefaultCapabilities,
            thinkingStyle: thinkingStyle ?? ClaudeThinkingStyle.Adaptive);

    private static LlmRouter CreateRouter(ILlmClient client) =>
        new(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>>
                {
                    ["claude-native"] = () => client
                },
                new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
                {
                    [("claude-native", ApiStyle.Claude)] = () => client
                }),
            new Dictionary<ModelStrategy, IReadOnlyList<string>>());

    private static LlmEndpointCapabilities DefaultCapabilities =>
        new()
        {
            NativeToolCalling = true,
            ParallelToolCalls = true,
            NativeStructuredOutput = false,
            StructuredOutputViaTool = true,
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
        HttpClient client,
        RecordingHandler handler)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name)
        {
            handler.ClientName = name;
            return client;
        }
    }

    private sealed class RecordingHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? responseHeaders = null,
        TimeSpan? retryAfter = null)
        : HttpMessageHandler
    {
        public string? ClientName { get; set; }

        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        public string? ApiKey { get; private set; }

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
            ApiKey = request.Headers.TryGetValues("x-api-key", out var apiKeyValues)
                ? apiKeyValues.FirstOrDefault()
                : null;

            var response = new HttpResponseMessage(
                statusCode)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "text/event-stream")
            };

            if (responseHeaders is not null)
            {
                foreach (var (name, value) in responseHeaders)
                {
                    response.Headers
                        .TryAddWithoutValidation(
                            name,
                            value);
                }
            }

            if (retryAfter is { } retry)
            {
                response.Headers.RetryAfter =
                    new System.Net.Http.Headers
                        .RetryConditionHeaderValue(retry);
            }

            return response;
        }
    }
}
