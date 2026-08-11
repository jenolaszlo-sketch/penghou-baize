using FluentAssertions;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Tests;

public sealed class LlmStreamingExtensionsTests
{
    private static readonly LlmRequest Request =
        new([new LlmMessage("user", "hello")]);

    [Fact]
    public async Task CompleteAsync_CollectsClientStreamWithoutRouterDependency()
    {
        var deltas = new List<string>();
        var client = new StreamingClient();

        var response = await client.CompleteAsync(
            Request,
            deltas.Add,
            TestContext.Current.CancellationToken);

        response.Content.Should().Be("hello");
        response.FinishReason.Should().Be("stop");
        deltas.Should().Equal("hel", "lo");
    }

    [Fact]
    public async Task CompleteAsync_PrefersOptionalNativeCompletion()
    {
        var implementation = new NativeClient();
        ILlmClient client = implementation;

        var response = await client.CompleteAsync(
            Request,
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("native");
        implementation.NativeCalls.Should().Be(1);
        implementation.StreamCalls.Should().Be(0);
    }

    [Fact]
    public async Task CompleteAsync_StreamsWhenDeltaCallbackIsRequested()
    {
        var client = new NativeClient();
        var deltas = new List<string>();

        var response = await client.CompleteAsync(
            Request,
            deltas.Add,
            TestContext.Current.CancellationToken);

        response.Content.Should().Be("stream");
        deltas.Should().Equal("stream");
        client.NativeCalls.Should().Be(0);
        client.StreamCalls.Should().Be(1);
    }

    private sealed class StreamingClient : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new LlmStreamEvent(Delta: "hel");
            yield return new LlmStreamEvent(Delta: "lo");
            yield return new LlmStreamEvent(FinishReason: "stop");
        }
    }

    private sealed class NativeClient : ILlmClient, ILlmCompletionClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();
        public int NativeCalls { get; private set; }
        public int StreamCalls { get; private set; }

        public Task<LlmResponse> CompleteAsync(
            LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            NativeCalls++;
            return Task.FromResult(new LlmResponse("native"));
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            await Task.Yield();
            yield return new LlmStreamEvent(Delta: "stream");
        }
    }
}
