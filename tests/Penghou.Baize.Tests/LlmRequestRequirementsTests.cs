using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class LlmRequestRequirementsTests
{
    [Fact]
    public void From_DerivesToolsParallelStructuredThinkingAndMediaRequirements()
    {
        var thinking = new LlmThinkingConfig(
            LlmThinkingMode.Enabled,
            LlmThinkingEffort.Medium);
        var request = new LlmRequest(
            [
                new LlmMessage("user",
                [
                    new LlmTextContent("inspect"),
                    new LlmReasoningContent("prior reasoning"),
                    new LlmImageContent(
                        "image/png",
                        new LlmInlineDataSource(new byte[] { 1 })),
                    new LlmAudioContent(
                        "audio/mpeg",
                        new LlmUriSource(new Uri("https://example.test/audio.mp3"))),
                    new LlmVideoContent(
                        "video/mp4",
                        new LlmProviderFileSource(new LlmProviderKey("test"), "video-1")),
                    new LlmFileContent(
                        "application/pdf",
                        new LlmInlineDataSource(new byte[] { 2 }),
                        "document.pdf")
                ]),
                LlmMessage.Assistant(
                [
                    new LlmToolCall("call-1", "first", "{}"),
                    new LlmToolCall("call-2", "second", "{}")
                ])
            ],
            responseFormat: LlmResponseFormat.Json(),
            thinkingConfig: thinking);

        var requirements = LlmRequestRequirements.From(request);

        requirements.ToolCalling.Should().BeTrue();
        requirements.ParallelToolCalls.Should().BeTrue();
        requirements.StructuredOutput.Should().BeTrue();
        requirements.Thinking.Should().BeSameAs(thinking);
        requirements.Content.Should().BeEquivalentTo(
        [
            new LlmContentRequirement(LlmContentType.Text),
            new LlmContentRequirement(
                LlmContentType.Image,
                LlmContentTransport.InlineData),
            new LlmContentRequirement(
                LlmContentType.Audio,
                LlmContentTransport.Uri),
            new LlmContentRequirement(
                LlmContentType.Video,
                LlmContentTransport.ProviderFile),
            new LlmContentRequirement(
                LlmContentType.File,
                LlmContentTransport.InlineData)
        ]);
    }

    [Fact]
    public void From_ToolResultReplayRequiresToolCallingAndDeduplicatesText()
    {
        var request = new LlmRequest(
        [
            new LlmMessage("user", "hello"),
            new LlmMessage("assistant", "again"),
            LlmMessage.ToolResult("call-1", "lookup", "done")
        ]);

        var requirements = LlmRequestRequirements.From(request);

        requirements.ToolCalling.Should().BeTrue();
        requirements.ParallelToolCalls.Should().BeFalse();
        requirements.Content.Should().Equal(
            new LlmContentRequirement(LlmContentType.Text));
    }

    [Fact]
    public void From_RejectsNullRequest()
    {
        var action = () => LlmRequestRequirements.From(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsSatisfiedBy_ReportsEachUnsupportedCapability()
    {
        AssertRejected(
            new LlmRequestRequirements { ToolCalling = true },
            new LlmEndpointCapabilities(),
            "native tool calling");
        AssertRejected(
            new LlmRequestRequirements { ParallelToolCalls = true },
            new LlmEndpointCapabilities(),
            "parallel tool calls");
        AssertRejected(
            new LlmRequestRequirements { StructuredOutput = true },
            new LlmEndpointCapabilities(),
            "structured output");
        AssertRejected(
            new LlmRequestRequirements
            {
                ToolCalling = true,
                StructuredOutput = true
            },
            new LlmEndpointCapabilities
            {
                NativeToolCalling = true,
                NativeStructuredOutput = true
            },
            "tools combined with structured output");
        AssertRejected(
            new LlmRequestRequirements
            {
                Thinking = new LlmThinkingConfig(LlmThinkingMode.Enabled)
            },
            new LlmEndpointCapabilities(),
            "extended thinking");
        AssertRejected(
            new LlmRequestRequirements
            {
                Thinking = new LlmThinkingConfig(
                    LlmThinkingMode.Enabled,
                    LlmThinkingEffort.Max)
            },
            new LlmEndpointCapabilities
            {
                Thinking = true,
                SupportedThinkingEfforts = new HashSet<LlmThinkingEffort>
                {
                    LlmThinkingEffort.Low
                }
            },
            "thinking effort 'Max'");
        AssertRejected(
            new LlmRequestRequirements
            {
                Thinking = new LlmThinkingConfig(LlmThinkingMode.Disabled)
            },
            new LlmEndpointCapabilities(),
            "explicitly disabling thinking");
        AssertRejected(
            new LlmRequestRequirements
            {
                Content = [new LlmContentRequirement(LlmContentType.Image)]
            },
            new LlmEndpointCapabilities(),
            "content type 'Image'");
        AssertRejected(
            new LlmRequestRequirements
            {
                Content =
                [
                    new LlmContentRequirement(
                        LlmContentType.Image,
                        LlmContentTransport.Uri)
                ]
            },
            new LlmEndpointCapabilities
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
            },
            "transport 'Uri' for 'Image'");
    }

    [Fact]
    public void IsSatisfiedBy_AcceptsCompleteCapabilityCombination()
    {
        var requirements = new LlmRequestRequirements
        {
            ToolCalling = true,
            ParallelToolCalls = true,
            StructuredOutput = true,
            Thinking = new LlmThinkingConfig(
                LlmThinkingMode.Enabled,
                LlmThinkingEffort.None),
            Content =
            [
                new LlmContentRequirement(
                    LlmContentType.Image,
                    LlmContentTransport.Uri)
            ]
        };
        var capabilities = new LlmEndpointCapabilities
        {
            NativeToolCalling = true,
            ParallelToolCalls = true,
            NativeStructuredOutput = true,
            ToolsWithStructuredOutput = true,
            Thinking = true,
            ContentTypes = new HashSet<LlmContentType>
            {
                LlmContentType.Text,
                LlmContentType.Image
            },
            ContentTransports = new Dictionary<LlmContentType, LlmContentTransport>
            {
                [LlmContentType.Image] =
                    LlmContentTransport.InlineData | LlmContentTransport.Uri
            }
        };

        requirements.IsSatisfiedBy(capabilities, out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsSatisfiedBy_RejectsNullCapabilities()
    {
        var requirements = new LlmRequestRequirements();
        var action = () => requirements.IsSatisfiedBy(null!, out _);

        action.Should().Throw<ArgumentNullException>();
    }

    private static void AssertRejected(
        LlmRequestRequirements requirements,
        LlmEndpointCapabilities capabilities,
        string reasonFragment)
    {
        requirements.IsSatisfiedBy(capabilities, out var reason).Should().BeFalse();
        reason.Should().Contain(reasonFragment);
    }
}
