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

    private sealed class RecordingClient(params LlmStreamEvent[] events) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();
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
