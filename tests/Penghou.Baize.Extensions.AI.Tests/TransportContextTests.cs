using Xunit;
using Microsoft.Extensions.AI;
using Penghou.Model.Abstractions;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Extensions.AI.Tests;

public sealed class TransportContextTests
{
    [Fact]
    public async Task ChatOptionsForwardContextAndUsageIntentWithoutConvertingThemToPromptData()
    {
        var inner = new RecordingClient();
        using var adapter = new BaizeChatClient(inner);
        var context = new ModelExecutionContext("workflow", "activity", "agent", "correlation");
        var intent = new ModelUsageIntent(10, 20, 0.1m, "USD");
        var options = new ChatOptions
        {
            AdditionalProperties = new()
            {
                [BaizeChatClient.ExecutionContextKey] = context,
                [BaizeChatClient.UsageIntentKey] = intent
            }
        };
        await foreach (var _ in adapter.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], options,
                           TestContext.Current.CancellationToken)) { }
        Assert.NotNull(inner.Request);
        Assert.Same(context, inner.Request.ExecutionContext);
        Assert.Same(intent, inner.Request.UsageIntent);
        Assert.Empty(inner.Request.Metadata);
        Assert.Equal("hello", Assert.IsType<LlmTextContent>(Assert.Single(Assert.Single(inner.Request.Messages).Parts)).Text);
    }

    [Fact]
    public async Task WrongContextTypeIsRejectedBeforeDispatch()
    {
        var inner = new RecordingClient();
        using var adapter = new BaizeChatClient(inner);
        var options = new ChatOptions { AdditionalProperties = new() { [BaizeChatClient.ExecutionContextKey] = "unbound-context" } };
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in adapter.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], options,
                               TestContext.Current.CancellationToken)) { }
        });
        Assert.Null(inner.Request);
    }

    private sealed class RecordingClient : ILlmClient
    {
        public LlmRequest? Request;
        public LlmEndpointCapabilities Capabilities { get; } = new();
        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            await Task.CompletedTask;
            yield break;
        }
    }
}
