using FluentAssertions;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace Penghou.Baize.Extensions.AI.Tests;

public sealed class BaizeChatClientTests
{
    [Fact]
    public async Task StreamingResponse_MapsTextAndUsage()
    {
        var inner = new RecordingClient(
            new LlmStreamEvent(Delta: "hello"),
            new LlmStreamEvent(Usage: new LlmUsage(3, 2, 5)));
        using var client = new BaizeChatClient(inner, "Test", modelId: "model");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.Select(update => update.Text).Should().Contain("hello");
        updates.SelectMany(update => update.Contents)
            .OfType<UsageContent>()
            .Single().Details.TotalTokenCount.Should().Be(5);
    }

    [Fact]
    public async Task Request_MapsInlineImageAndOptions()
    {
        var inner = new RecordingClient();
        using var client = new BaizeChatClient(inner, "Gemini");
        var message = new ChatMessage(
            ChatRole.User,
            [
                new TextContent("describe"),
                new DataContent(new byte[] { 1, 2 }, "image/png")
            ]);

        await foreach (var _ in client.GetStreamingResponseAsync(
            [message],
            new ChatOptions { Temperature = 0.2f, MaxOutputTokens = 100 },
            TestContext.Current.CancellationToken)) { }

        inner.Request.Should().NotBeNull();
        inner.Request!.Temperature.Should().BeApproximately(0.2, 0.000001);
        inner.Request.MaxTokens.Should().Be(100);
        inner.Request.Messages[0].Parts
            .OfType<LlmImageContent>()
            .Should().ContainSingle()
            .Which.Source.Should().BeOfType<LlmInlineDataSource>();
    }

    [Fact]
    public async Task StreamingResponse_EmitsToolCallsBeforeFinish()
    {
        var inner = new RecordingClient(
            new LlmStreamEvent(ToolCallDelta: new ToolCallDelta(
                0, "call-1", "lookup", "{\"city\":")),
            new LlmStreamEvent(ToolCallDelta: new ToolCallDelta(
                0, ArgumentsJsonFragment: "\"Paris\"}")),
            new LlmStreamEvent(FinishReason: "tool_calls"));
        using var client = new BaizeChatClient(inner);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "weather")],
                           cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(update);

        var callIndex = updates.FindIndex(update =>
            update.Contents.OfType<FunctionCallContent>().Any());
        var finishIndex = updates.FindIndex(update => update.FinishReason is not null);
        callIndex.Should().BeGreaterThanOrEqualTo(0);
        finishIndex.Should().BeGreaterThan(callIndex);
    }

    [Fact]
    public async Task StreamingResponse_PreservesMalformedToolArgumentsAsRawContent()
    {
        var inner = new RecordingClient(
            new LlmStreamEvent(ToolCallDelta: new ToolCallDelta(
                0, "call-1", "lookup", "{not-json")),
            new LlmStreamEvent(FinishReason: "tool_calls"));
        using var client = new BaizeChatClient(inner);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "weather")],
                           cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(update);

        updates.SelectMany(update => update.Contents)
            .OfType<FunctionCallContent>()
            .Single().Arguments!["$raw"].Should().Be("{not-json");
    }

    [Fact]
    public async Task Request_PreservesPlainJsonFormatAndStringToolResult()
    {
        var inner = new RecordingClient();
        using var client = new BaizeChatClient(inner);
        var messages = new[]
        {
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", "lookup")]),
            new ChatMessage(
                ChatRole.Tool,
                [new FunctionResultContent("call-1", "plain text")])
        };

        await foreach (var _ in client.GetStreamingResponseAsync(
                           messages,
                           new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                           TestContext.Current.CancellationToken))
        {
        }

        inner.Request!.ResponseFormat!.Type.Should().Be("json_object");
        inner.Request.ResponseFormat.Schema.Should().BeNull();
        inner.Request.Messages.SelectMany(message => message.Parts)
            .OfType<LlmToolResultContent>()
            .Single().Result.Content.Should().Be("plain text");
    }

    [Fact]
    public async Task StreamingResponse_ExposesClientMetadataAndDiagnostics()
    {
        var diagnostics = new LlmProviderDiagnostics("Test", ActualModel: "actual");
        var inner = new RecordingClient(new LlmStreamEvent(Diagnostics: diagnostics));
        using var client = new BaizeChatClient(inner);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(update);

        client.GetService(typeof(ChatClientMetadata))
            .Should().BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be("test-model");
        updates.Should().ContainSingle().Which.AdditionalProperties!
            .Should().ContainKey("baize.provider_diagnostics")
            .WhoseValue.Should().BeSameAs(diagnostics);
    }

    [Fact]
    public async Task Request_MapsInstructionsToolsSchemaAndMaximumReasoning()
    {
        using var schemaDocument = JsonDocument.Parse(
            """{"type":"object","properties":{"city":{"type":"string"}}}""");
        var schema = schemaDocument.RootElement.Clone();
        var declaration = AIFunctionFactory.CreateDeclaration(
            "lookup",
            "Looks up a city",
            schema);
        var inner = new RecordingClient();
        using var client = new BaizeChatClient(inner);

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "weather")],
                           new ChatOptions
                           {
                               Instructions = "Be exact",
                               Tools = [declaration],
                               ResponseFormat = ChatResponseFormat.ForJsonSchema(schema),
                               Reasoning = new ReasoningOptions
                               {
                                   Effort = ReasoningEffort.ExtraHigh
                               }
                           },
                           TestContext.Current.CancellationToken))
        {
        }

        inner.Request!.Messages[0].Role.Should().Be("system");
        inner.Request.Messages[0].Parts.OfType<LlmTextContent>()
            .Single().Text.Should().Be("Be exact");
        inner.Request.Tools.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new LlmTool("lookup", "Looks up a city", schema.GetRawText()));
        inner.Request.ResponseFormat!.Type.Should().Be("json_schema");
        inner.Request.ResponseFormat.Schema.Should().Be(schema.GetRawText());
        inner.Request.ThinkingConfig.Should().BeEquivalentTo(
            new LlmThinkingConfig(
                LlmThinkingMode.Enabled,
                LlmThinkingEffort.Max));
    }

    [Fact]
    public async Task Request_MapsInlineUriAndHostedMediaByTopLevelType()
    {
        var inner = new RecordingClient();
        using var client = new BaizeChatClient(inner, "Gemini");
        var hosted = new HostedFileContent("provider-file")
        {
            MediaType = "audio/wav",
            Name = "voice.wav"
        };
        var message = new ChatMessage(
            ChatRole.User,
            [
                new DataContent(new byte[] { 1 }, "audio/wav"),
                new DataContent(new byte[] { 2 }, "video/mp4"),
                new DataContent(new byte[] { 3 }, "application/pdf"),
                new UriContent(new Uri("https://example.test/image.png"), "image/png"),
                hosted
            ]);

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [message],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        var parts = inner.Request!.Messages.Single().Parts;
        parts[0].Should().BeOfType<LlmAudioContent>()
            .Which.Source.Should().BeOfType<LlmInlineDataSource>();
        parts[1].Should().BeOfType<LlmVideoContent>()
            .Which.Source.Should().BeOfType<LlmInlineDataSource>();
        parts[2].Should().BeOfType<LlmFileContent>()
            .Which.Source.Should().BeOfType<LlmInlineDataSource>();
        parts[3].Should().BeOfType<LlmImageContent>()
            .Which.Source.Should().BeOfType<LlmUriSource>();
        var hostedPart = parts[4].Should().BeOfType<LlmAudioContent>().Which;
        hostedPart.Source.Should().BeOfType<LlmProviderFileSource>()
            .Which.Provider.Should().Be(new LlmProviderKey("Gemini"));
    }

    [Fact]
    public async Task Request_MapsToolResultShapesAndFailureState()
    {
        using var jsonDocument = JsonDocument.Parse("{\"value\":2}");
        var failed = new FunctionResultContent("call-object", new { value = 3 })
        {
            Exception = new InvalidOperationException("failed")
        };
        var messages = new[]
        {
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new FunctionCallContent("call-null", "null_tool"),
                    new FunctionCallContent("call-json", "json_tool"),
                    new FunctionCallContent("call-object", "object_tool")
                ]),
            new ChatMessage(
                ChatRole.Tool,
                [
                    new FunctionResultContent("call-null", null),
                    new FunctionResultContent("call-json", jsonDocument.RootElement.Clone()),
                    failed
                ])
        };
        var inner = new RecordingClient();
        using var client = new BaizeChatClient(inner);

        await foreach (var _ in client.GetStreamingResponseAsync(
                           messages,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        var results = inner.Request!.Messages.SelectMany(message => message.Parts)
            .OfType<LlmToolResultContent>()
            .Select(content => content.Result)
            .ToArray();
        results.Should().HaveCount(3);
        results[0].ToolName.Should().Be("null_tool");
        results[0].Content.Should().Be("null");
        results[1].Content.Should().Be("{\"value\":2}");
        results[2].Content.Should().Be("{\"value\":3}");
        results[2].Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task StreamingResponse_MaterializesPendingAndEmptyToolCallsAtEnd()
    {
        var inner = new RecordingClient(
            new LlmStreamEvent(ToolCallDelta: new ToolCallDelta(
                2, null, null, null)),
            new LlmStreamEvent(ToolCallDelta: new ToolCallDelta(
                1, "call-1", "lookup", null)));
        using var client = new BaizeChatClient(inner, modelId: "adapter-model");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "weather")],
                           cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(update);

        var calls = updates.SelectMany(update => update.Contents)
            .OfType<FunctionCallContent>()
            .ToArray();
        calls.Should().HaveCount(2);
        calls[0].CallId.Should().Be("call-1");
        calls[0].Name.Should().Be("lookup");
        calls[0].Arguments.Should().BeEmpty();
        calls[1].CallId.Should().NotBeNullOrWhiteSpace();
        calls[1].Name.Should().BeEmpty();
        updates.Should().OnlyContain(update => update.ModelId == "adapter-model");
    }

    [Fact]
    public void GetService_RespectsKeysTypesMetadataAndNullGuards()
    {
        var inner = new RecordingClient();
        using var client = new BaizeChatClient(inner, "Override", modelId: "override-model");

        client.GetService(typeof(BaizeChatClient)).Should().BeSameAs(client);
        client.GetService(typeof(ILlmClient)).Should().BeSameAs(inner);
        client.GetService(typeof(ChatClientMetadata)).Should()
            .BeOfType<ChatClientMetadata>()
            .Which.ProviderName.Should().Be("Override");
        client.GetService(typeof(ChatClientMetadata), "key").Should().BeNull();
        client.GetService(typeof(IDisposable)).Should().BeSameAs(client);
        client.GetService(typeof(IFormatProvider)).Should().BeNull();
        FluentActions.Invoking(() => client.GetService(null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new BaizeChatClient(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Request_RejectsUnsupportedExtensionsAiContent()
    {
        using var client = new BaizeChatClient(new RecordingClient());
        var message = new ChatMessage(ChatRole.User, [new UnsupportedContent()]);

        var action = async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync(
                               [message],
                               cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        };

        await action.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*UnsupportedContent*");
    }

    private sealed class RecordingClient(params LlmStreamEvent[] events)
        : ILlmClient, ILlmClientMetadataProvider
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();
        public LlmClientMetadata Metadata { get; } = new(
            "Test", "test-model", new Uri("https://test.example"));
        public LlmRequest? Request { get; private set; }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Request = request;
            foreach (var item in events)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class UnsupportedContent : AIContent;
}
