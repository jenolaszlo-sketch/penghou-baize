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

    [Fact]
    public async Task CollectAsync_PreservesOrderedPartsContinuationsAndDiagnostics()
    {
        var reasoningContinuation = new LlmProviderContinuation(
            "Test",
            new Dictionary<string, string> { ["signature"] = "reasoning" });
        var updatedReasoningContinuation = new LlmProviderContinuation(
            "Test",
            new Dictionary<string, string> { ["signature"] = "updated" });
        var contentContinuation = new LlmProviderContinuation(
            "Test",
            new Dictionary<string, string> { ["signature"] = "content" });
        var toolContinuation = new LlmProviderContinuation(
            "Test",
            new Dictionary<string, string> { ["signature"] = "tool" });
        var usage = new LlmUsage(10, 4, 14, ThinkingTokens: 2);
        var diagnostics = new LlmProviderDiagnostics(
            "Test",
            ActualModel: "actual-model",
            ResponseId: "response-1");
        var routerDiagnostics = new LlmRouterDiagnostics(
            [
                new LlmRouterAttempt(
                    "endpoint-1",
                    "logical-model",
                    "Test",
                    LlmRouterAttemptOutcome.Succeeded,
                    TimeSpan.FromMilliseconds(12))
            ]);
        var attempts = new[]
        {
            new LlmRepairAttempt(
                "tolerant-parser",
                LlmRepairStatus.Succeeded,
                Repaired: "{}")
        };
        var repairDiagnostics = new LlmJsonRepairDiagnostics(
            LlmRepairShapeStatus.Matched,
            [],
            SucceededBy: "tolerant-parser");

        var response = await Events(
            new LlmStreamEvent(
                ReasoningContent: "think",
                Continuation: reasoningContinuation)
            { PartIndex = 0 },
            new LlmStreamEvent(Continuation: updatedReasoningContinuation)
            {
                PartIndex = 0
            },
            new LlmStreamEvent(Delta: "hel", Continuation: contentContinuation)
            {
                PartIndex = 1
            },
            new LlmStreamEvent(Delta: "lo") { PartIndex = 1 },
            new LlmStreamEvent(
                ToolCallDelta: new ToolCallDelta(
                    0,
                    "call-1",
                    "lookup",
                    "{\"id\":"))
            { PartIndex = 2 },
            new LlmStreamEvent(
                ToolCallDelta: new ToolCallDelta(
                    0,
                    null,
                    null,
                    "7}",
                    toolContinuation))
            { PartIndex = 2 },
            new LlmStreamEvent(
                FinishReason: "stop",
                Usage: usage,
                Diagnostics: diagnostics,
                RouterDiagnostics: routerDiagnostics)
            {
                ContentWasRepaired = true,
                ContentRepairAttempts = attempts,
                ContentRepairDiagnostics = repairDiagnostics
            }).CollectAsync(cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("hello");
        response.Reasoning.Should().Be("think");
        response.FinishReason.Should().Be("stop");
        response.Usage.Should().BeSameAs(usage);
        response.Diagnostics.Should().BeSameAs(diagnostics);
        response.RouterDiagnostics.Should().BeSameAs(routerDiagnostics);
        response.ContentWasRepaired.Should().BeTrue();
        response.ContentRepairAttempts.Should().BeSameAs(attempts);
        response.ContentRepairDiagnostics.Should().BeSameAs(repairDiagnostics);
        response.ReasoningContinuation.Should().BeSameAs(updatedReasoningContinuation);
        response.ContentContinuation.Should().BeSameAs(contentContinuation);
        response.ToolCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new LlmToolCall(
                "call-1",
                "lookup",
                "{\"id\":7}",
                Continuation: toolContinuation));
        response.Parts.Should().HaveCount(3);
        response.Parts![0].Should().BeEquivalentTo(
            new LlmReasoningContent("think")
            {
                Continuation = updatedReasoningContinuation
            });
        response.Parts[1].Should().BeEquivalentTo(
            new LlmTextContent("hello") { Continuation = contentContinuation });
        response.Parts[2].Should().BeOfType<LlmToolCallContent>();
    }

    [Fact]
    public async Task CollectAsync_UnindexedPartsGroupOnlyAdjacentMatchingKinds()
    {
        var response = await Events(
            new LlmStreamEvent(Delta: "a"),
            new LlmStreamEvent(Delta: "b"),
            new LlmStreamEvent(ReasoningContent: "c"),
            new LlmStreamEvent(ReasoningContent: "d"),
            new LlmStreamEvent(Delta: "e"))
            .CollectAsync(cancellationToken: TestContext.Current.CancellationToken);

        response.Parts.Should().Equal(
            new LlmTextContent("ab"),
            new LlmReasoningContent("cd"),
            new LlmTextContent("e"));
    }

    [Fact]
    public async Task CollectAsync_RejectsPartWhoseKindChanges()
    {
        var action = () => Events(
                new LlmStreamEvent(Delta: "text") { PartIndex = 4 },
                new LlmStreamEvent(ReasoningContent: "reasoning") { PartIndex = 4 })
            .CollectAsync(cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<LlmClientException>()
            .Where(exception => exception.FailureKind == LlmClientFailureKind.Protocol)
            .WithMessage("*changed from Text to Reasoning*");
    }

    [Fact]
    public async Task CollectAsync_RejectsTwoToolCallsAssignedToOnePart()
    {
        var action = () => Events(
                new LlmStreamEvent(
                    ToolCallDelta: new ToolCallDelta(0, "one", "first", "{}"))
                {
                    PartIndex = 2
                },
                new LlmStreamEvent(
                    ToolCallDelta: new ToolCallDelta(1, "two", "second", "{}"))
                {
                    PartIndex = 2
                })
            .CollectAsync(cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<LlmClientException>()
            .Where(exception => exception.FailureKind == LlmClientFailureKind.Protocol)
            .WithMessage("*more than one tool call*");
    }

    [Fact]
    public async Task CollectAsync_IgnoresIncompleteNamelessToolCall()
    {
        var response = await Events(
                new LlmStreamEvent(
                    ToolCallDelta: new ToolCallDelta(0, "call-1", null, "{}")))
            .CollectAsync(cancellationToken: TestContext.Current.CancellationToken);

        response.ToolCalls.Should().BeEmpty();
        response.Parts.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAndCollectAsync_RejectNullArguments()
    {
        ILlmClient? client = null;
        IAsyncEnumerable<LlmStreamEvent>? stream = null;

        await FluentActions.Awaiting(() => client!.CompleteAsync(Request))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => new StreamingClient().CompleteAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => stream!.CollectAsync())
            .Should().ThrowAsync<ArgumentNullException>();
    }

    private static async IAsyncEnumerable<LlmStreamEvent> Events(
        params LlmStreamEvent[] events)
    {
        await Task.Yield();
        foreach (var item in events)
            yield return item;
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
