using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class LlmStreamAssemblerTests
{
    private const string Marker = "<|tool-call-marker|x>";

    [Fact]
    public void Accept_PreservesCanonicalCharactersExactly()
    {
        var assembler = new LlmStreamAssembler();
        const string content = "\n\r\n  \t雪🙂 trailing  ";

        var events = assembler.Accept(new(
            new LlmStreamEvent(Delta: content),
            ProviderCharacterCount: content.Length));
        var completion = assembler.Complete(new(
            StreamTerminalKind.FinishReason,
            ProtocolCompleted: true,
            FinishReason: "stop"));

        events.Should().ContainSingle().Which.Delta.Should().Be(content);
        completion.Events.Should().BeEmpty();
        completion.Error.Should().BeNull();
        completion.Diagnostics.NormalizedCharacterCount.Should().Be(content.Length);
        completion.Diagnostics.EmittedCharacterCount.Should().Be(content.Length);
        completion.Diagnostics.BufferedCharacterCount.Should().Be(0);
        completion.Diagnostics.IsConserved.Should().BeTrue();
    }

    [Fact]
    public void Marker_IsRecognizedAtEveryPossibleChunkBoundary()
    {
        for (var split = 1; split < Marker.Length; split++)
        {
            var assembler = new LlmStreamAssembler([Marker]);
            var output = new List<LlmStreamEvent>();
            output.AddRange(Accept(assembler, $"before{Marker[..split]}"));
            output.AddRange(Accept(assembler, $"{Marker[split..]}after"));
            var completion = Complete(assembler);
            output.AddRange(completion.Events);

            string.Concat(output.Select(item => item.Delta))
                .Should().Be("beforeafter", $"marker split {split} must be lossless");
            completion.Diagnostics.ConsumedProtocolCharacterCount
                .Should().Be(Marker.Length);
            completion.Diagnostics.BufferedCharacterCount.Should().Be(0);
            completion.Diagnostics.IsConserved.Should().BeTrue();
            completion.Error.Should().BeNull();
        }
    }

    [Fact]
    public void MarkerImmediatelyBeforeCompletion_IsConsumed()
    {
        var assembler = new LlmStreamAssembler([Marker]);
        var output = Accept(assembler, $"text{Marker}").ToList();

        var completion = Complete(assembler);
        output.AddRange(completion.Events);

        string.Concat(output.Select(item => item.Delta)).Should().Be("text");
        completion.Diagnostics.ConsumedProtocolCharacterCount
            .Should().Be(Marker.Length);
        completion.Diagnostics.BufferedCharacterCount.Should().Be(0);
        completion.Diagnostics.IsConserved.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    public void MarkerLengthBoundaries_PreserveNonMarkers(int difference)
    {
        var length = Math.Max(0, Marker.Length + difference);
        var content = new string('x', length);
        var assembler = new LlmStreamAssembler([Marker]);
        var output = Accept(assembler, content).ToList();

        var completion = Complete(assembler);
        output.AddRange(completion.Events);

        string.Concat(output.Select(item => item.Delta)).Should().Be(content);
        completion.Diagnostics.BufferedCharacterCount.Should().Be(0);
        completion.Diagnostics.ConsumedProtocolCharacterCount.Should().Be(0);
        completion.Diagnostics.IsConserved.Should().BeTrue();
    }

    [Fact]
    public void Complete_ReleasesEveryProperMarkerPrefixAsText()
    {
        for (var length = 1; length < Marker.Length; length++)
        {
            var prefix = Marker[..length];
            var assembler = new LlmStreamAssembler([Marker]);
            var output = Accept(assembler, prefix).ToList();
            var completion = Complete(assembler);
            output.AddRange(completion.Events);

            string.Concat(output.Select(item => item.Delta)).Should().Be(prefix);
            completion.Diagnostics.BufferedCharacterCount.Should().Be(0);
            completion.Diagnostics.ConsumedProtocolCharacterCount.Should().Be(0);
            completion.Diagnostics.IsConserved.Should().BeTrue();
        }
    }

    [Fact]
    public void Complete_ReleasesTwentyCharacterLookaheadAndPreservesLeadingNewline()
    {
        const string twentyCharacterPrefix = "<|tool-call-marker|x";
        twentyCharacterPrefix.Should().HaveLength(20);
        var content = "\nleading content and a final tail " +
                      twentyCharacterPrefix;
        var assembler = new LlmStreamAssembler([Marker]);
        var streamed = Accept(assembler, content).ToList();

        assembler.Snapshot().BufferedCharacterCount.Should().Be(20);
        var completion = assembler.Complete(new(
            StreamTerminalKind.MessageStop,
            ProtocolCompleted: true,
            FinishReason: "end_turn"));
        streamed.AddRange(completion.Events);

        string.Concat(streamed.Select(item => item.Delta)).Should().Be(content);
        string.Concat(completion.Events.Select(item => item.Delta))
            .Should().Be(twentyCharacterPrefix);
        completion.Diagnostics.BufferedCharacterCount.Should().Be(0);
        completion.Diagnostics.FinishReason.Should().Be("end_turn");
        completion.Diagnostics.IsConserved.Should().BeTrue();
        completion.Error.Should().BeNull();
    }

    [Fact]
    public void UnicodeSurrogatePairSplitAcrossDeltas_IsPreservedByCodeUnit()
    {
        const string content = "雪🙂tail";
        var split = content.IndexOf('\ud83d') + 1;
        var assembler = new LlmStreamAssembler([Marker]);
        var output = Accept(assembler, content[..split]).ToList();
        output.AddRange(Accept(assembler, content[split..]));

        var completion = Complete(assembler);
        output.AddRange(completion.Events);

        string.Concat(output.Select(item => item.Delta)).Should().Be(content);
        completion.Diagnostics.NormalizedCharacterCount.Should().Be(content.Length);
        completion.Diagnostics.IsConserved.Should().BeTrue();
    }

    [Fact]
    public void BoundaryOnlyAndEmptyDeltas_AreCountedWithoutInventingCharacters()
    {
        var assembler = new LlmStreamAssembler();
        assembler.Accept(new(
            Event: null,
            ProviderCharacterCount: 17,
            ProviderChunkCount: 1)).Should().BeEmpty();
        assembler.Accept(new(
            new LlmStreamEvent(Delta: string.Empty),
            ProviderCharacterCount: 2,
            ProviderChunkCount: 0)).Should().ContainSingle();

        var completion = Complete(assembler);

        completion.Diagnostics.ProviderChunkCount.Should().Be(1);
        completion.Diagnostics.ProviderCharacterCount.Should().Be(19);
        completion.Diagnostics.NormalizedCharacterCount.Should().Be(0);
        completion.Diagnostics.EmittedCharacterCount.Should().Be(0);
        completion.Diagnostics.IsConserved.Should().BeTrue();
    }

    [Fact]
    public void CleanTerminal_DoesNotHideIncompleteToolCall()
    {
        var assembler = new LlmStreamAssembler();
        assembler.Accept(new(
            new LlmStreamEvent(
                ToolCallDelta: new ToolCallDelta(
                    2,
                    Id: "call-2",
                    ArgumentsJsonFragment: "{}")),
            ProviderCharacterCount: 2));

        var completion = Complete(assembler);

        completion.Error.Should().NotBeNull();
        completion.Error!.FailureKind.Should().Be(LlmClientFailureKind.Protocol);
        completion.Diagnostics.ToolCallCount.Should().Be(1);
        completion.Diagnostics.ProtocolWarnings.Should().ContainSingle(value =>
            value.Code == "stream.tool-call.name-missing");
        completion.Diagnostics.IsConserved.Should().BeTrue();
    }

    [Fact]
    public void IncompleteTerminal_FlushesTextBeforeAvailabilityError()
    {
        var assembler = new LlmStreamAssembler([Marker]);
        var output = Accept(assembler, Marker[..^1]).ToList();

        var completion = assembler.Complete(new(
            StreamTerminalKind.EndOfStream,
            ProtocolCompleted: false));
        output.AddRange(completion.Events);

        string.Concat(output.Select(item => item.Delta)).Should().Be(Marker[..^1]);
        completion.Diagnostics.BufferedCharacterCount.Should().Be(0);
        completion.Diagnostics.IsConserved.Should().BeTrue();
        completion.Error.Should().NotBeNull();
        completion.Error!.FailureKind.Should().Be(LlmClientFailureKind.Availability);
    }

    [Fact]
    public void ToolFragments_AreCountedOnceAndRemainExact()
    {
        var assembler = new LlmStreamAssembler();
        var first = assembler.Accept(new(
            new LlmStreamEvent(
                ToolCallDelta: new ToolCallDelta(
                    0,
                    Id: "call-1",
                    Name: "lookup",
                    ArgumentsJsonFragment: "{\"id\":")),
            ProviderCharacterCount: 6));
        var second = assembler.Accept(new(
            new LlmStreamEvent(
                ToolCallDelta: new ToolCallDelta(
                    0,
                    ArgumentsJsonFragment: "7}")),
            ProviderCharacterCount: 2));

        var completion = Complete(assembler);

        string.Concat(first.Concat(second)
                .Select(item => item.ToolCallDelta?.ArgumentsJsonFragment))
            .Should().Be("{\"id\":7}");
        completion.Diagnostics.ToolCallCount.Should().Be(1);
        completion.Diagnostics.NormalizedCharacterCount.Should().Be(8);
        completion.Diagnostics.EmittedCharacterCount.Should().Be(8);
        completion.Diagnostics.IsConserved.Should().BeTrue();
        completion.Error.Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsAmbiguousMarkerPrefixes()
    {
        var action = () => new LlmStreamAssembler(["<tool>", "<tool>"]);
        action.Should().NotThrow();

        var ambiguous = () => new LlmStreamAssembler(["<tool>", "<tool>call"]);
        ambiguous.Should().Throw<ArgumentException>()
            .WithMessage("*prefix-free*");
    }

    private static IReadOnlyList<LlmStreamEvent> Accept(
        LlmStreamAssembler assembler,
        string content) =>
        assembler.Accept(new(
            new LlmStreamEvent(Delta: content),
            ProviderCharacterCount: content.Length));

    private static StreamAssemblyCompletion Complete(
        LlmStreamAssembler assembler) =>
        assembler.Complete(new(
            StreamTerminalKind.DoneSentinel,
            ProtocolCompleted: true,
            FinishReason: "stop"));
}
