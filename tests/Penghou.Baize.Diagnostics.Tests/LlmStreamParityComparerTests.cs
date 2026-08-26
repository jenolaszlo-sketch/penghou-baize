using FluentAssertions;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Diagnostics.Tests;

public sealed class LlmStreamParityComparerTests
{
    private static readonly LlmRequest Request =
        new([new LlmMessage("user", "deterministic")]);

    public static TheoryData<string> ExactWhitespaceCases => new()
    {
        "\nleading newline",
        "\r\nleading CRLF",
        " leading space",
        "   multiple leading spaces",
        "\tleading tab",
        "\n\nmultiple newlines",
        "trailing whitespace \t\r\n"
    };

    [Theory]
    [MemberData(nameof(ExactWhitespaceCases))]
    public async Task CompareAsync_PreservesAndComparesExactCharacters(string content)
    {
        var result = await LlmStreamParityComparer.CompareAsync(
            new DeterministicClient(content, content),
            Request,
            TestContext.Current.CancellationToken);

        result.IsExactMatch.Should().BeTrue();
        result.StreamedCharacterCount.Should().Be(content.Length);
        result.NonStreamingCharacterCount.Should().Be(content.Length);
        result.FirstDivergenceIndex.Should().BeNull();
    }

    [Fact]
    public async Task CompareAsync_ReportsFirstChangedCharacterWithoutRetainingContent()
    {
        var result = await LlmStreamParityComparer.CompareAsync(
            new DeterministicClient("\nabc-tail", "\nabd"),
            Request,
            TestContext.Current.CancellationToken);

        result.IsExactMatch.Should().BeFalse();
        result.StreamedCharacterCount.Should().Be(9);
        result.NonStreamingCharacterCount.Should().Be(4);
        result.FirstDivergenceIndex.Should().Be(3);
    }

    [Fact]
    public async Task CompareAsync_ReportsShorterLengthForPrefixDivergence()
    {
        var result = await LlmStreamParityComparer.CompareAsync(
            new DeterministicClient("same plus tail", "same"),
            Request,
            TestContext.Current.CancellationToken);

        result.IsExactMatch.Should().BeFalse();
        result.FirstDivergenceIndex.Should().Be(4);
    }

    [Fact]
    public async Task CompareAsync_RequiresNativeCompletionCapability()
    {
        var action = () => LlmStreamParityComparer.CompareAsync(
            new StreamingOnlyClient(),
            Request,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("client")
            .WithMessage("*ILlmCompletionClient*");
    }

    private sealed class DeterministicClient(
        string streamed,
        string nonStreaming) : ILlmClient, ILlmCompletionClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public Task<LlmResponse> CompleteAsync(
            LlmRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmResponse(nonStreaming));

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var midpoint = streamed.Length / 2;
            yield return new LlmStreamEvent(Delta: streamed[..midpoint]);
            yield return new LlmStreamEvent(Delta: streamed[midpoint..]);
        }
    }

    private sealed class StreamingOnlyClient : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new LlmStreamEvent(Delta: "stream");
        }
    }
}
