using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class MultimodalValidationTests
{
    [Fact]
    public void Validate_AcceptsConfiguredImageTransport()
    {
        var request = new LlmRequest(
        [
            new LlmMessage("user",
            [
                new LlmTextContent("describe this"),
                new LlmImageContent(
                    "image/png",
                    new LlmInlineDataSource(new byte[] { 1, 2, 3 }))
            ])
        ]);
        var capabilities = new LlmEndpointCapabilities
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

        var action = () => LlmRequestValidator.Validate("vision", capabilities, request);

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsUnsupportedMediaTransport()
    {
        var request = new LlmRequest(
        [
            new LlmMessage("user",
            [
                new LlmImageContent(
                    "image/png",
                    new LlmUriSource(new Uri("https://example.test/image.png")))
            ])
        ]);
        var capabilities = new LlmEndpointCapabilities
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

        var action = () => LlmRequestValidator.Validate("vision", capabilities, request);

        action.Should().Throw<LlmRequestValidationException>()
            .WithMessage("*does not support transport*Uri*Image*");
    }

    [Fact]
    public void InlineSource_TakesSnapshotOfBytes()
    {
        byte[] bytes = [1, 2, 3];
        var source = new LlmInlineDataSource(bytes);

        bytes[0] = 9;

        source.Data.Span[0].Should().Be(1);
    }
}
