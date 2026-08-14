using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;
using System.Text.Json;

namespace Penghou.Baize.Gemini.Tests;

public sealed class GeminiGenerationClientTests
{
    [Fact]
    public async Task SubmitAsync_TextToImage_PostsInteractionsAndMapsImageAsset()
    {
        var handler = new RecordingHandler().ReturnJson(
            """
            {
              "id": "interaction-1",
              "model": "gemini-3.1-flash-lite-image",
              "status": "completed",
              "output_image": {
                "type": "image",
                "mime_type": "image/png",
                "data": "aGVsbG8="
              }
            }
            """);
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt = "a red circle",
                AspectRatio = "1:1",
                OutputFormat = "png"
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Handle.Provider.Should().Be("Gemini");
        operation.Handle.Id.Should().Be("interaction-1");
        var asset = operation.Result!.Assets.Should().ContainSingle().Subject;
        asset.ContentType.Should().Be("image/png");
        asset.Source.Should().BeOfType<InlineGeneratedAssetSource>();
        asset.Source.As<InlineGeneratedAssetSource>().Data.ToArray().Should().Equal("hello"u8.ToArray());

        handler.Requests.Should().HaveCount(1);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1beta/interactions");
        handler.LastRequest!.Headers.Should().Contain(
            header => header.Key == "x-goog-api-key" &&
                      header.Value.Single() == "secret");

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("model").GetString().Should().Be("gemini-3.1-flash-lite-image");
        root.GetProperty("store").GetBoolean().Should().BeFalse();
        var input = root.GetProperty("input");
        input[0].GetProperty("type").GetString().Should().Be("text");
        input[0].GetProperty("text").GetString().Should().Be("a red circle");
        var format = root.GetProperty("response_format");
        format.GetProperty("type").GetString().Should().Be("image");
        format.GetProperty("mime_type").GetString().Should().Be("image/png");
        format.GetProperty("aspect_ratio").GetString().Should().Be("1:1");
    }

    [Fact]
    public async Task SubmitAsync_ImageEdit_AddsInlineAndUriParts()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"interaction-2","status":"completed","output_image":{"type":"image","mime_type":"image/png","data":"aGVsbG8="}}""");
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt = "make it red",
                Inputs =
                [
                    new LlmInlineDataSource("fake-bytes"u8.ToArray()),
                    new LlmUriSource(new Uri("https://ref.test/reference.png"))
                ]
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var input = body.RootElement.GetProperty("input");
        input[0].GetProperty("type").GetString().Should().Be("text");
        input[1].GetProperty("type").GetString().Should().Be("image");
        input[1].GetProperty("data").GetString().Should().Be(Convert.ToBase64String("fake-bytes"u8.ToArray()));
        input[2].GetProperty("type").GetString().Should().Be("image");
        input[2].GetProperty("uri").GetString().Should().Be("https://ref.test/reference.png");
    }

    [Fact]
    public async Task SubmitAsync_NonCompletedStatus_ReturnsUnknownOperation()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"interaction-3","status":"pending","steps":[]}""");
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "a blue square" },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Unknown);
        operation.Handle.Id.Should().Be("interaction-3");
    }

    [Fact]
    public async Task SubmitAsync_CompletedWithoutImage_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"interaction-4","status":"completed","steps":[]}""");
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "nothing" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task SubmitAsync_VideoRequest_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "clip" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_AlwaysRejected()
    {
        var client = CreateClient(new RecordingHandler());
        var handle = new GenerationOperationHandle("Gemini", "gemini-gen-1", "op-1");

        var action = async () => await client.GetAsync(
            handle,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
    }

    [Fact]
    public async Task CancelAsync_AlwaysRejected()
    {
        var client = CreateClient(new RecordingHandler());
        var handle = new GenerationOperationHandle("Gemini", "gemini-gen-1", "op-1");

        var action = async () => await client.CancelAsync(
            handle,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
    }

    [Fact]
    public async Task GetAsync_HandleFromAnotherEndpoint_ThrowsInvalidRequest()
    {
        var client = CreateClient(new RecordingHandler());
        var handle = new GenerationOperationHandle("Gemini", "other-endpoint", "op-1");

        var action = async () => await client.GetAsync(
            handle,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.InvalidRequest);
    }

    [Fact]
    public void AddBaizeGeminiGeneration_RegistersKeyedClientAndRegistry()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddBaizeGeneration();
        services.AddBaizeGeminiGeneration("gemini-gen-1", options =>
        {
            options.Model = "gemini-3.1-flash-lite-image";
            options.ApiKey = "secret";
        });

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<IGenerationClient>("gemini-gen-1");
        client.Capabilities.Features.Should().Be(
            GenerationFeature.TextToImage | GenerationFeature.ImageToImage);
        var registry = provider.GetRequiredService<IGenerationClientRegistry>();
        registry.Find("Gemini", "gemini-gen-1").Should().NotBeNull();
    }

    private static GeminiGenerationClient CreateClient(RecordingHandler handler) =>
        new(
            model: "gemini-3.1-flash-lite-image",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            "https://generativelanguage.googleapis.com/v1beta",
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToImage | GenerationFeature.ImageToImage,
                InputTransports = new HashSet<LlmContentTransport>
                {
                    LlmContentTransport.Uri,
                    LlmContentTransport.InlineData
                }
            },
            "gemini-gen-1");
}
