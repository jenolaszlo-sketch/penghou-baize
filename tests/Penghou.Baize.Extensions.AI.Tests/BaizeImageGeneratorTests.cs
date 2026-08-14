using FluentAssertions;
using Microsoft.Extensions.AI;
using Penghou.Baize.Generation;
using Xunit;

#pragma warning disable MEAI001

namespace Penghou.Baize.Extensions.AI.Tests;

public sealed class BaizeImageGeneratorTests
{
    private static readonly GenerationOperationHandle Handle =
        new("Test", "img", "op-1", "img-model");

    [Fact]
    public async Task GenerateAsync_TextToImage_MapsRequestAndInlineAsset()
    {
        var operation = new GenerationOperation(
            Handle,
            GenerationOperationState.Succeeded,
            new GenerationResult(
                [new GeneratedAsset(
                    new InlineGeneratedAssetSource(new byte[] { 9, 8, 7 }, "image/png"),
                    ContentType: "image/png")]));
        var inner = new FakeGenerationClient(operation);
        using var generator = new BaizeImageGenerator(inner, "OpenAi", modelId: "img-model");

        var response = await generator.GenerateAsync(
            new Microsoft.Extensions.AI.ImageGenerationRequest("a blue circle"),
            new ImageGenerationOptions
            {
                Count = 2,
                ImageSize = new System.Drawing.Size(1024, 768),
                MediaType = "image/jpeg"
            },
            TestContext.Current.CancellationToken);

        var submitted = (Penghou.Baize.Generation.ImageGenerationRequest)inner.Submitted!;
        submitted.Prompt.Should().Be("a blue circle");
        submitted.Count.Should().Be(2);
        submitted.Size.Should().Be(new GenerationImageSize(1024, 768));
        submitted.OutputFormat.Should().Be("image/jpeg");
        submitted.Inputs.Should().BeEmpty();

        var content = response.Contents.Should().ContainSingle()
            .Which.Should().BeOfType<DataContent>().Which;
        content.MediaType.Should().Be("image/png");
        content.Data.ToArray().Should().Equal(9, 8, 7);
        response.RawRepresentation.Should().BeSameAs(operation);
    }

    [Fact]
    public async Task GenerateAsync_ImageEdit_MapsOriginalImages()
    {
        var inner = new FakeGenerationClient(new GenerationOperation(
            Handle,
            GenerationOperationState.Succeeded,
            new GenerationResult([])));
        using var generator = new BaizeImageGenerator(inner, "Gemini");
        var request = new Microsoft.Extensions.AI.ImageGenerationRequest(
            "add a hat",
            [
                new DataContent(new byte[] { 1, 2 }, "image/png"),
                new UriContent(new Uri("https://example.test/in.png"), "image/png")
            ]);

        var response = await generator.GenerateAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        var submitted = (Penghou.Baize.Generation.ImageGenerationRequest)inner.Submitted!;
        submitted.Inputs.Should().HaveCount(2);
        submitted.Inputs[0].Should().BeOfType<LlmInlineDataSource>();
        submitted.Inputs[1].Should().BeOfType<LlmUriSource>();
        response.Contents.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_ThrowsBaizeExceptionOnFailedOperation()
    {
        var inner = new FakeGenerationClient(new GenerationOperation(
            Handle,
            GenerationOperationState.Failed,
            Error: new GenerationError(
                GenerationErrorKind.RateLimited,
                "slow down",
                StatusCode: 429,
                ProviderStatus: "rate_limited")));
        using var generator = new BaizeImageGenerator(inner);

        var action = async () => await generator.GenerateAsync(
            new Microsoft.Extensions.AI.ImageGenerationRequest("a cube"),
            cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await action.Should().ThrowAsync<BaizeException>())
            .Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.RateLimited);
        exception.Message.Should().Be("slow down");
        exception.ProviderStatus.Should().Be("rate_limited");
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnsupportedInputContent()
    {
        var inner = new FakeGenerationClient(new GenerationOperation(
            Handle,
            GenerationOperationState.Succeeded,
            new GenerationResult([])));
        using var generator = new BaizeImageGenerator(inner);
        var request = new Microsoft.Extensions.AI.ImageGenerationRequest(
            "edit", [new UnsupportedContent()]);

        var action = async () => await generator.GenerateAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*UnsupportedContent*");
    }

    [Fact]
    public void GetService_RespectsKeysTypesMetadataAndNullGuards()
    {
        var inner = new FakeGenerationClient(new GenerationOperation(
            Handle,
            GenerationOperationState.Succeeded,
            new GenerationResult([])));
        using var generator = new BaizeImageGenerator(inner, "Override", modelId: "override-model");

        generator.GetService(typeof(BaizeImageGenerator)).Should().BeSameAs(generator);
        generator.GetService(typeof(IGenerationClient)).Should().BeSameAs(inner);
        generator.GetService(typeof(ImageGeneratorMetadata)).Should()
            .BeOfType<ImageGeneratorMetadata>()
            .Which.ProviderName.Should().Be("Override");
        generator.GetService(typeof(ImageGeneratorMetadata), "key").Should().BeNull();
        generator.GetService(typeof(IDisposable)).Should().BeSameAs(generator);
        generator.GetService(typeof(IFormatProvider)).Should().BeNull();
        FluentActions.Invoking(() => generator.GetService(null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new BaizeImageGenerator(null!))
            .Should().Throw<ArgumentNullException>();
    }

    private sealed class FakeGenerationClient(GenerationOperation operation)
        : IGenerationClient
    {
        public GenerationCapabilities Capabilities { get; } = new()
        {
            Features = GenerationFeature.TextToImage |
                       GenerationFeature.ImageToImage |
                       GenerationFeature.MultipleCandidates
        };

        public GenerationOperation SubmittedOperation => operation;
        public GenerationRequest? Submitted { get; private set; }

        public Task<GenerationOperation> SubmitAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Submitted = request;
            return Task.FromResult(operation);
        }

        public Task<GenerationOperation> GetAsync(
            GenerationOperationHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(operation);

        public Task<GenerationOperation> CancelAsync(
            GenerationOperationHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(operation);
    }

    private sealed class UnsupportedContent : AIContent;
}
