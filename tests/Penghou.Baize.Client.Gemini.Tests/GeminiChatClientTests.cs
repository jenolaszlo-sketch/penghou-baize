using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize;
using Penghou.Baize.Gemini;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Gemini.Tests;

public sealed class GeminiChatClientTests
{
    [Fact]
    public async Task StreamAsync_MapsTextAndToolCallDeltas()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Hello"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":5,"totalTokenCount":15}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");
        var request = new LlmRequest(
            [new LlmMessage("user", "Say hello")],
            temperature: 0.1,
            maxTokens: 2048);

        var events = await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current
                    .CancellationToken));

        events.Where(item =>
                item.Delta != null)
            .Select(item => item.Delta)
            .Should()
            .Equal("Hello");
        events.Should().Contain(
            item =>
                item.FinishReason == "stop");
        events.Should().Contain(
            item =>
                item.Usage != null);
    }

    [Fact]
    public async Task StreamAsync_MapsNativeToolCalls()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"emit_files","args":{"files":[]}}}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":12,"candidatesTokenCount":8,"totalTokenCount":20}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");
        var request = new LlmRequest(
            [new LlmMessage("user", "Generate files")],
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

        var events = await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current
                    .CancellationToken));

        var toolCall = events
            .Single(item =>
                item.ToolCallDelta is not null)
            .ToolCallDelta!;
        toolCall.Index.Should().Be(0);
        toolCall.Name.Should().Be("emit_files");
        using var arguments =
            JsonDocument.Parse(
                toolCall.ArgumentsJsonFragment!);
        arguments.RootElement
            .GetProperty("files")
            .GetArrayLength()
            .Should()
            .Be(0);
        events.Should().Contain(
            item =>
                item.FinishReason == "stop");
    }

    [Fact]
    public async Task StreamAsync_RejectsStreamWithoutFinalChunk()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[]},"finishReason":null}]}

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    new LlmRequest(
                        [new LlmMessage(
                            "user",
                            "hello")]),
                    TestContext.Current
                        .CancellationToken));

        var exception = await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage(
                "*Gemini stream ended without a final chunk*");
        exception.Which.FailureKind
            .Should().Be(LlmClientFailureKind.Availability);
    }

    [Fact]
    public async Task StreamAsync_ThrowsForToolsWhenUnsupported()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash",
            DefaultCapabilities with { NativeToolCalling = false });
        var request = new LlmRequest(
            [new LlmMessage("user", "Generate files")],
            tools:
            [
                new LlmTool(
                    "emit_files",
                    "Emits files",
                    """{"type":"object"}""")
            ]);

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current
                        .CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support native tool calling*");
    }

    [Fact]
    public async Task StreamAsync_SurfacesThoughtPartsAsReasoning()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"internal reasoning","thought":true},{"text":"visible answer"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":6,"candidatesTokenCount":4,"totalTokenCount":10}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");
        var request = new LlmRequest(
            [new LlmMessage("user", "Solve it")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.Medium));

        var events = await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        events.Single(item =>
                item.ReasoningContent is not null)
            .ReasoningContent.Should()
            .Be("internal reasoning");
        events.Single(item => item.Delta is not null)
            .Delta.Should().Be("visible answer");
    }

    [Fact]
    public async Task StreamAsync_CapturesThoughtSignatureContinuation()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"internal reasoning","thought":true,"thoughtSignature":"sig_abc"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":6,"candidatesTokenCount":4,"totalTokenCount":10}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-flash");
        var request = new LlmRequest(
            [new LlmMessage("user", "Solve it")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.Medium));

        var events = await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        var reasoningEvent = events.Single(item =>
            item.ReasoningContent is not null);
        reasoningEvent.Continuation.Should().NotBeNull();
        reasoningEvent.Continuation!.Provider.Should().Be("Gemini");
        reasoningEvent.Continuation
            .GetValue("thoughtSignature").Should().Be("sig_abc");
    }

    [Fact]
    public async Task StreamAsync_RoundTripsThoughtSignatureOnNextTurn()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":1,"candidatesTokenCount":1,"totalTokenCount":2}}

            data: [DONE]

            """);
        var client = CreateClient(handler, "gemini-2.5-flash");
        var request = new LlmRequest(
            [
                new LlmMessage(
                    "assistant",
                    new List<LlmContentPart>
                    {
                        new LlmReasoningContent("internal reasoning")
                        {
                            Continuation =
                                new LlmProviderContinuation(
                                    "Gemini",
                                    new Dictionary<string, string>
                                    {
                                        ["thoughtSignature"] = "sig_abc"
                                    })
                        },
                        new LlmToolCallContent(
                            new LlmToolCall(
                                "call_1",
                                "get_weather",
                                """{"city":"Paris"}"""))
                    }),
                LlmMessage.ToolResults(
                    [
                        new LlmToolResult(
                            "call_1",
                            "get_weather",
                            """{"temp":21}""")
                    ])
            ]);

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var document =
            JsonDocument.Parse(handler.RequestBody!);
        var contents = document.RootElement
            .GetProperty("contents");
        var assistantParts = contents[0]
            .GetProperty("parts");
        assistantParts[0].GetProperty("thought")
            .GetBoolean().Should().BeTrue();
        assistantParts[0].GetProperty("thoughtSignature")
            .GetString().Should().Be("sig_abc");
        assistantParts[1].GetProperty("functionCall")
            .GetProperty("id")
            .GetString().Should().Be("call_1");
        contents[1].GetProperty("parts")[0]
            .GetProperty("functionResponse")
            .GetProperty("id")
            .GetString().Should().Be("call_1");
    }

    [Fact]
    public async Task StreamAsync_MovesSystemMessagesToSystemInstruction()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");
        var request = new LlmRequest(
            [
                new LlmMessage("system", "First system"),
                new LlmMessage("system", "Second system"),
                new LlmMessage("user", "hi")
            ]);

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        var root = requestDocument.RootElement;
        root.TryGetProperty(
            "systemInstruction",
            out var systemInstruction)
            .Should().BeTrue();
        var instructionText = systemInstruction
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        instructionText.Should().Contain("First system");
        instructionText.Should().Contain("Second system");

        var contents = root.GetProperty("contents");
        contents.GetArrayLength().Should().Be(1);
        contents[0].GetProperty("role")
            .GetString().Should().Be("user");
    }

    [Fact]
    public async Task StreamAsync_OmitsSystemInstructionWhenNoSystemMessages()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");

        await CollectAsync(
            client.StreamAsync(
                new LlmRequest(
                    [new LlmMessage("user", "hi")]),
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        requestDocument.RootElement
            .TryGetProperty("systemInstruction", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_RejectsNonTextSystemParts()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");
        var request = new LlmRequest(
            [
                new LlmMessage(
                    "system",
                    [new LlmReasoningContent("reason")]),
                new LlmMessage("user", "hi")
            ]);

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current.CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*only text in the system instruction*");
    }

    [Fact]
    public async Task StreamAsync_CapturesContinuationOnFunctionCallPart()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"functionCall":{"id":"fc_1","name":"emit_files","args":{"files":[]}},"thoughtSignature":"sig_fc"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":6,"candidatesTokenCount":4,"totalTokenCount":10}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-flash");

        var events = await CollectAsync(
            client.StreamAsync(
                new LlmRequest(
                    [new LlmMessage("user", "Generate files")]),
                TestContext.Current.CancellationToken));

        var toolDelta = events.Single(item =>
            item.ToolCallDelta is not null).ToolCallDelta!;
        toolDelta.Continuation.Should().NotBeNull();
        toolDelta.Continuation!
            .GetValue("thoughtSignature")
            .Should().Be("sig_fc");
        events.Single(item =>
                item.ToolCallDelta is not null)
            .Continuation!.GetValue("thoughtSignature")
            .Should().Be("sig_fc");
    }

    [Fact]
    public async Task StreamAsync_CapturesContinuationOnPlainTextPart()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"preface","thoughtSignature":"sig_txt"},{"text":"answer"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":6,"candidatesTokenCount":4,"totalTokenCount":10}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-pro");

        var events = await CollectAsync(
            client.StreamAsync(
                new LlmRequest(
                    [new LlmMessage("user", "Solve")]),
                TestContext.Current.CancellationToken));

        var deltas = events
            .Where(e => e.Delta is not null)
            .ToList();
        deltas.Should().HaveCount(2);
        deltas[0].Delta.Should().Be("preface");
        deltas[0].Continuation.Should().NotBeNull();
        deltas[0].Continuation!
            .GetValue("thoughtSignature")
            .Should().Be("sig_txt");
        deltas[1].Delta.Should().Be("answer");
        deltas[1].Continuation.Should().BeNull();
    }

    [Fact]
    public async Task CompleteStreamingAsync_PreservesContinuationAcrossContentAndToolCalls()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"preface","thoughtSignature":"sig_txt"},{"text":"answer"},{"functionCall":{"id":"fc_1","name":"emit_files","args":{}},"thoughtSignature":"sig_fc"},{"text":"","thoughtSignature":"sig_tail"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":6,"candidatesTokenCount":4,"totalTokenCount":10}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-pro");
        var router = new LlmRouter(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>>
                {
                    ["gemini-native"] = () => client
                },
                new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
                {
                    [("gemini-native", ApiStyle.Gemini)] = () => client
                }),
            new Dictionary<
                ModelStrategy,
                IReadOnlyList<string>>());

        var response =
            await router.CompleteStreamingAsync(
                "gemini-native",
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Generate")]
                },
                cancellationToken:
                    TestContext.Current
                        .CancellationToken);

        response.ContentContinuation.Should().NotBeNull();
        response.ContentContinuation!
            .GetValue("thoughtSignature")
            .Should().Be("sig_tail");
        response.ToolCalls.Should().HaveCount(1);
        response.ToolCalls![0].Continuation
            .Should().NotBeNull();
        response.ToolCalls[0].Continuation!
            .GetValue("thoughtSignature")
            .Should().Be("sig_fc");

        // The signed content part and the signed tool-call part are retained as
        // distinct, ordered parts with their own continuations instead of being
        // merged into one string.
        response.Content.Should().Be("prefaceanswer");
        response.Parts.Should().HaveCount(4);
        response.Parts![0].Should().BeOfType<LlmTextContent>()
            .Which.Text.Should().Be("preface");
        response.Parts[0].Continuation!
            .GetValue("thoughtSignature")
            .Should().Be("sig_txt");
        response.Parts[1].Should().BeOfType<LlmTextContent>()
            .Which.Text.Should().Be("answer");
        response.Parts[1].Continuation.Should().BeNull();
        response.Parts[2].Should().BeOfType<LlmToolCallContent>()
            .Which.ToolCall.Continuation!
            .GetValue("thoughtSignature")
            .Should().Be("sig_fc");
        var signatureOnly = response.Parts[3]
            .Should().BeOfType<LlmTextContent>().Subject;
        signatureOnly.Text.Should().BeEmpty();
        signatureOnly.Continuation!.GetValue("thoughtSignature")
            .Should().Be("sig_tail");
    }

    [Fact]
    public async Task StreamAsync_ReplaysFunctionCallSignatureOnNextTurn()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":1,"candidatesTokenCount":1,"totalTokenCount":2}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-pro");
        var request = new LlmRequest(
            [
                new LlmMessage(
                    "assistant",
                    [
                        new LlmToolCallContent(
                            new LlmToolCall(
                                "call_1",
                                "get_weather",
                                """{"city":"Paris"}""",
                                Continuation:
                                    new LlmProviderContinuation(
                                        "Gemini",
                                        new Dictionary<string, string>
                                        {
                                            ["thoughtSignature"] =
                                                "sig_replay"
                                        })))
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
        var part =
            document.RootElement
                .GetProperty("contents")[0]
                .GetProperty("parts")[0];
        part.GetProperty("functionCall")
            .GetProperty("id")
            .GetString().Should().Be("call_1");
        part.GetProperty("thoughtSignature")
            .GetString().Should().Be("sig_replay");
    }

    [Fact]
    public async Task StreamAsync_ReplaysTextSignatureOnNextTurn()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":1,"candidatesTokenCount":1,"totalTokenCount":2}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-pro");
        var request = new LlmRequest(
            [
                new LlmMessage(
                    "assistant",
                    [
                        new LlmTextContent("preface")
                        {
                            Continuation =
                                new LlmProviderContinuation(
                                    "Gemini",
                                    new Dictionary<string, string>
                                    {
                                        ["thoughtSignature"] =
                                            "sig_txt"
                                    })
                        }
                    ])
            ]);

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var document =
            JsonDocument.Parse(handler.RequestBody!);
        document.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[0]
            .GetProperty("thoughtSignature")
            .GetString().Should().Be("sig_txt");
    }

    [Fact]
    public async Task StreamAsync_RejectsNoneEffortWithoutThinkingBudget()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");
        var request = new LlmRequest(
            [new LlmMessage("user", "Say ok")],
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
            .WithMessage(
                "*thinking token budget*instead of 'None'*");
    }

    [Fact]
    public async Task StreamAsync_UsesExplicitBudgetWhenNoEffortGiven()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash",
            DefaultCapabilities with
            {
                ThinkingBudget = 2048
            });
        var request = new LlmRequest(
            [new LlmMessage("user", "Say ok")],
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
        requestDocument.RootElement
            .GetProperty("generationConfig")
            .GetProperty("thinkingConfig")
            .GetProperty("thinkingBudget")
            .GetInt32()
            .Should().Be(2048);
    }

    [Fact]
    public async Task StreamAsync_ExplicitThinkingBudgetWinsOverEffortMapping()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-pro",
            DefaultCapabilities with
            {
                ThinkingBudget = 16384
            });
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
            .GetProperty("generationConfig")
            .GetProperty("thinkingConfig")
            .GetProperty("thinkingBudget")
            .GetInt32()
            .Should().Be(16384);
    }

    [Fact]
    public async Task StreamAsync_MaxEffortUsesDocumentedBudgetCeiling()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-pro");
        var request = new LlmRequest(
            [new LlmMessage("user", "Reason hard")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled,
                    effort: LlmThinkingEffort.Max));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        requestDocument.RootElement
            .GetProperty("generationConfig")
            .GetProperty("thinkingConfig")
            .GetProperty("thinkingBudget")
            .GetInt32()
            .Should().Be(32768);
    }

    [Fact]
    public async Task StreamAsync_ThrowsForDisabledThinkingWhenUnsupported()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");
        var request = new LlmRequest(
            [new LlmMessage("user", "Say ok")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Disabled,
                    effort: LlmThinkingEffort.Medium));

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
    public async Task StreamAsync_EmitsZeroBudgetForExplicitlyDisabledThinking()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":5,"candidatesTokenCount":2,"totalTokenCount":7}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash",
            DefaultCapabilities with
            {
                ThinkingDisable = true
            });
        var request = new LlmRequest(
            [new LlmMessage("user", "Say ok")],
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
            .GetProperty("generationConfig")
            .GetProperty("thinkingConfig")
            .GetProperty("thinkingBudget")
            .GetInt32()
            .Should().Be(0);
    }

    [Fact]
    public async Task CompleteStreamingAsync_PreservesGeminiDiagnostics()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"result"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":8,"candidatesTokenCount":3,"totalTokenCount":11}}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");
        var router = new LlmRouter(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>>
                {
                    ["gemini-native"] = () => client
                },
                new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
                {
                    [("gemini-native", ApiStyle.Gemini)] = () => client
                }),
            new Dictionary<
                ModelStrategy,
                IReadOnlyList<string>>());

        var response =
            await router.CompleteStreamingAsync(
                "gemini-native",
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Generate")]
                },
                cancellationToken:
                    TestContext.Current
                        .CancellationToken);

        response.FinishReason.Should().Be("stop");
        response.Usage.Should().NotBeNull();
        response.Usage!.PromptTokens.Should().Be(8);
        response.Usage.CompletionTokens.Should().Be(3);
        response.Usage.TotalTokens.Should().Be(11);
    }

    [Fact]
    public void AddLlmRouting_MapsAliasToNativeGeminiClient()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LlmRouting:Models:0:Name"] =
                            "gemini-alias",
                        ["LlmRouting:Models:0:Endpoints:0:ApiStyle"] =
                            "Gemini",
                        ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] =
                            "gemini-2.0-flash",
                        ["LlmRouting:Models:0:Endpoints:0:BaseUrl"] =
                            "https://generativelanguage.googleapis.com"
                    })
                .Build();
        var services = new ServiceCollection();
        services.AddHttpClient("llm");
        services.AddGeminiLlmProvider();
        services.AddLlmRouting(configuration);

        using var provider =
            services.BuildServiceProvider();
        var models = provider.GetRequiredService<ILlmModelLookup>();

        var metadata = models.GetClient("gemini-alias")
            .Should().BeAssignableTo<ILlmClientMetadataProvider>()
            .Subject.Metadata;
        metadata.Provider.Should().Be("Gemini");
        metadata.Model.Should().Be("gemini-2.0-flash");
        metadata.Endpoint.Should().Be(
            new Uri("https://generativelanguage.googleapis.com"));
    }

    [Fact]
    public async Task StreamAsync_RoundTripsToolCallConversation()
    {
        var handler = new RecordingHandler(
            """data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":1,"candidatesTokenCount":1,"totalTokenCount":2}}""");
        var client = CreateClient(handler, "gemini-2.0-flash");
        var request = new LlmRequest(
            [
                LlmMessage.Assistant(
                    [new LlmToolCall("call_1", "get_weather", """{"city":"Paris"}""")]),
                LlmMessage.ToolResults(
                    [new LlmToolResult("call_1", "get_weather", """{"temp":21}""")])
            ]);

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var document =
            JsonDocument.Parse(handler.RequestBody!);
        var contents = document.RootElement
            .GetProperty("contents");
        contents.GetArrayLength().Should().Be(2);

        contents[0].GetProperty("role")
            .GetString().Should().Be("model");
        var functionCall = contents[0]
            .GetProperty("parts")[0]
            .GetProperty("functionCall");
        functionCall.GetProperty("id")
            .GetString().Should().Be("call_1");
        functionCall.GetProperty("name")
            .GetString().Should().Be("get_weather");
        functionCall.GetProperty("args")
            .GetProperty("city")
            .GetString().Should().Be("Paris");

        contents[1].GetProperty("role")
            .GetString().Should().Be("user");
        var functionResponse = contents[1]
            .GetProperty("parts")[0]
            .GetProperty("functionResponse");
        functionResponse.GetProperty("id")
            .GetString().Should().Be("call_1");
        functionResponse.GetProperty("name")
            .GetString().Should().Be("get_weather");
        functionResponse.GetProperty("response")
            .GetProperty("temp")
            .GetInt32().Should().Be(21);
    }

    [Fact]
    public async Task StreamAsync_UsesStreamGenerateContentEndpoint()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-flash");

        await CollectAsync(
            client.StreamAsync(
                new LlmRequest(
                    [new LlmMessage("user", "hi")]),
                TestContext.Current.CancellationToken));

        handler.RequestUri!.AbsoluteUri.Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?alt=sse");
    }

    [Fact]
    public async Task StreamAsync_DoesNotDuplicateVersionSegmentInEndpoint()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-flash",
            baseUrl:
                "https://generativelanguage.googleapis.com/v1beta");

        await CollectAsync(
            client.StreamAsync(
                new LlmRequest(
                    [new LlmMessage("user", "hi")]),
                TestContext.Current.CancellationToken));

        handler.RequestUri!.AbsoluteUri.Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?alt=sse");
    }

    [Fact]
    public async Task StreamAsync_AuthenticatesWithApiKeyHeader()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-flash");

        await CollectAsync(
            client.StreamAsync(
                new LlmRequest(
                    [new LlmMessage("user", "hi")]),
                TestContext.Current.CancellationToken));

        handler.RequestHeaders
            .Should().ContainKey("x-goog-api-key")
            .WhoseValue.Should().Be("test-key");
        handler.RequestHeaders
            .Should().NotContainKey("Authorization");
    }

    [Fact]
    public async Task StreamAsync_DoesNotSendModelOrStreamInBody()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-flash");

        await CollectAsync(
            client.StreamAsync(
                new LlmRequest(
                    [new LlmMessage("user", "hi")]),
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        requestDocument.RootElement
            .TryGetProperty("model", out _)
            .Should().BeFalse();
        requestDocument.RootElement
            .TryGetProperty("stream", out _)
            .Should().BeFalse();
        requestDocument.RootElement
            .GetProperty("contents")
            .ValueKind.Should()
            .Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task StreamAsync_SetsJsonMimeTypeForStructuredOutput()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"{}"}]},"finishReason":"STOP"}]}

            data: [DONE]

            """);
        var client = CreateClient(
            handler,
            "gemini-2.5-flash");
        var request = new LlmRequest(
            [new LlmMessage("user", "Return the shape")],
            responseFormat:
                LlmResponseFormat.JsonSchema(
                    """{"type":"object"}"""));

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var requestDocument =
            JsonDocument.Parse(handler.RequestBody!);
        var generationConfig =
            requestDocument.RootElement
                .GetProperty("generationConfig");
        generationConfig.GetProperty("responseMimeType")
            .GetString().Should().Be("application/json");
        generationConfig.GetProperty("responseSchema")
            .ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task StreamAsync_MapsSchemaLessJsonWithoutResponseSchema()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"{}"}]},"finishReason":"STOP"}]}

            data: [DONE]

            """);
        var client = CreateClient(handler, "gemini-2.5-flash");

        await CollectAsync(client.StreamAsync(
            new LlmRequest(
                [new LlmMessage("user", "Return JSON")],
                responseFormat: LlmResponseFormat.Json()),
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var config = document.RootElement.GetProperty("generationConfig");
        config.GetProperty("responseMimeType").GetString()
            .Should().Be("application/json");
        config.TryGetProperty("responseSchema", out _).Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_RejectsStreamWithPartialContentButNoTerminal()
    {
        // A stream that emits partial text deltas but never reports a finish
        // reason or the [DONE] sentinel is truncated; it must not be accepted
        // as a complete answer even though it produced content.
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"partial"}]},"finishReason":null}]}
            """);
        var client = CreateClient(
            handler,
            "gemini-2.0-flash");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    new LlmRequest([new LlmMessage("user", "hello")]),
                    TestContext.Current.CancellationToken));

        var exception = await action.Should()
            .ThrowAsync<LlmClientException>();
        exception.WithMessage("*ended without a final chunk*");
        exception.Which.FailureKind
            .Should().Be(LlmClientFailureKind.Availability);
    }

    [Fact]
    public async Task StreamAsync_SerializesInlineImageContent()
    {
        var handler = new RecordingHandler(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"described"}]},"finishReason":"STOP"}]}

            data: [DONE]

            """);
        var capabilities = DefaultCapabilities with
        {
            ContentTypes = new HashSet<LlmContentType>
            {
                LlmContentType.Text,
                LlmContentType.Image
            },
            ContentTransports = new Dictionary<LlmContentType, LlmContentTransport>
            {
                [LlmContentType.Image] = LlmContentTransport.InlineData
            }
        };
        var client = CreateClient(handler, "gemini-2.0-flash", capabilities);
        var request = new LlmRequest(
            [
                new LlmMessage(
                    "user",
                    [
                        new LlmTextContent("describe"),
                        new LlmImageContent(
                            "image/png",
                            new LlmInlineDataSource(new byte[] { 1, 2, 3 }))
                    ])
            ]);

        await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var inline = document.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[1]
            .GetProperty("inlineData");
        inline.GetProperty("mimeType").GetString().Should().Be("image/png");
        inline.GetProperty("data").GetString().Should().Be("AQID");
    }

    private static GeminiChatClient CreateClient(
        RecordingHandler handler,
        string model,
        LlmEndpointCapabilities? capabilities = null,
        string baseUrl =
            "https://generativelanguage.googleapis.com") =>
        new(
            model,
            new TestHttpClientFactory(
                new HttpClient(handler)),
            apiKey: "test-key",
            baseUrl: baseUrl,
            capabilities: capabilities ?? DefaultCapabilities);

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

        public IReadOnlyDictionary<string, string>
            RequestHeaders
        { get; private set; } =
            new Dictionary<string, string>();

        protected override async Task<
            HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestHeaders =
                request.Headers.ToDictionary(
                    header => header.Key,
                    header =>
                        string.Join(
                            ",",
                            header.Value));
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
                    "application/json")
            };
        }
    }
}
