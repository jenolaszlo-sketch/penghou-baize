using FluentAssertions;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
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
}
