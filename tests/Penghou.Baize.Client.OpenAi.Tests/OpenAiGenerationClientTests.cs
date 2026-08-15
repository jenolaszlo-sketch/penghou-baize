using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.OpenAi.Tests;

public sealed class OpenAiGenerationClientTests
{
    [Fact]
    public async Task SubmitAsync_ImageGeneration_SendsImagesGenerations()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"created":123,"data":[{"url":"https://openai.test/1.png"}]}""");
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt = "a red circle",
                Count = 1,
                Size = new GenerationImageSize(1024, 1024),
                OutputFormat = "png",
                Seed = 42
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        var asset = operation.Result!.Assets[0];
        asset.Source.Should().BeOfType<UriGeneratedAssetSource>();
        asset.Source.As<UriGeneratedAssetSource>().Uri.ToString().Should().Be("https://openai.test/1.png");
        asset.ContentType.Should().Be("image/png");

        handler.Requests.Should().HaveCount(1);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/images/generations");
        handler.LastRequest!.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "secret"));

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("model").GetString().Should().Be("gpt-image-1");
        root.GetProperty("prompt").GetString().Should().Be("a red circle");
        root.GetProperty("n").GetInt32().Should().Be(1);
        root.GetProperty("size").GetString().Should().Be("1024x1024");
        root.GetProperty("output_format").GetString().Should().Be("png");
        root.GetProperty("seed").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task SubmitAsync_ImageEdit_WithInputs_SendsMultipartToImagesEdits()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"created":123,"data":[{"b64_json":"aGVsbG8="}]}""");
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
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/images/edits");
        var form = handler.LastRequestBody!;
        form.Should().Contain("name=prompt");
        form.Should().Contain("make it red");
        form.Should().Contain("name=image");
        form.Should().Contain("name=reference_image");
        form.Should().Contain("https://ref.test/reference.png");
    }

    [Fact]
    public async Task SubmitAsync_Video_ReturnsQueuedOperation()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"video_1","status":"queued","progress":0.0}""");
        var client = CreateClient(handler, video: true);

        var operation = await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "a dog running" },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Queued);
        operation.Handle.Id.Should().Be("video_1");
        operation.Handle.Provider.Should().Be("OpenAi");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/videos");
    }

    [Fact]
    public async Task GetAsync_Video_MapsQueuedRunningThenCompleted()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"id":"video_1","status":"queued","progress":0.0}""")
            .ReturnJson("""{"id":"video_1","status":"in_progress","progress":0.5}""")
            .ReturnJson(
                """{"id":"video_1","status":"completed","progress":1.0,"content":[{"url":"https://cdn.test/v.mp4","type":"video/mp4"}]}""");
        var client = CreateClient(handler, video: true);

        var submitted = await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "a dog running" },
            TestContext.Current.CancellationToken);

        var running = await client.GetAsync(submitted.Handle, TestContext.Current.CancellationToken);
        running.State.Should().Be(GenerationOperationState.Running);
        running.Progress.Should().Be(0.5);

        var completed = await client.GetAsync(submitted.Handle, TestContext.Current.CancellationToken);
        completed.State.Should().Be(GenerationOperationState.Succeeded);
        completed.Result!.Assets.Should().ContainSingle()
            .Which.Source.As<UriGeneratedAssetSource>().Uri.ToString().Should().Be("https://cdn.test/v.mp4");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/videos/video_1");
    }

    [Fact]
    public async Task GetAsync_Video_UnknownStatus_StaysUnknown()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"id":"video_1","status":"queued"}""")
            .ReturnJson("""{"id":"video_1","status":"processing_stuff"}""");
        var client = CreateClient(handler, video: true);

        var submitted = await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "a dog" },
            TestContext.Current.CancellationToken);

        var operation = await client.GetAsync(submitted.Handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Unknown);
    }

    [Fact]
    public async Task SubmitAsync_Speech_ReturnsInlineAudioAsset()
    {
        var handler = new RecordingHandler().ReturnBytes(
            [1, 2, 3, 4],
            "audio/mpeg");
        var client = CreateClient(handler, audio: true);

        var operation = await client.SubmitAsync(
            new AudioGenerationRequest
            {
                Prompt = "Hello world",
                Kind = AudioGenerationKind.Speech,
                Voice = "nova",
                OutputFormat = "mp3"
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        var asset = operation.Result!.Assets[0];
        asset.Source.Should().BeOfType<InlineGeneratedAssetSource>();
        asset.ContentType.Should().Be("audio/mpeg");
        asset.Source.As<InlineGeneratedAssetSource>().Data.ToArray().Should().Equal(1, 2, 3, 4);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/audio/speech");
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("model").GetString().Should().Be("tts-1");
        body.RootElement.GetProperty("input").GetString().Should().Be("Hello world");
        body.RootElement.GetProperty("voice").GetString().Should().Be("nova");
        body.RootElement.GetProperty("response_format").GetString().Should().Be("mp3");
    }

    [Fact]
    public async Task GetAsync_HandleFromAnotherEndpoint_ThrowsInvalidRequest()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler, video: true);
        var foreign = new GenerationOperationHandle("OpenAi", "other-endpoint", "video_1");

        var action = async () => await client.GetAsync(foreign, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.InvalidRequest);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AddBaizeOpenAiGeneration_RegistersKeyedClientAndRegistry()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("llm");
        services.AddBaizeOpenAiGeneration("ep-1", options =>
        {
            options.ApiKey = "secret";
            options.Model = "gpt-image-1";
        });

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<IGenerationClient>("ep-1");
        client.Capabilities.Features.Should().HaveFlag(GenerationFeature.TextToImage);
        client.Capabilities.Features.Should().HaveFlag(GenerationFeature.Cancellation);

        provider.GetRequiredService<IGenerationClientRegistry>()
            .Find("OpenAi", "ep-1").Should().BeSameAs(client);
    }

    [Fact]
    public void AddBaizeOpenAiGeneration_MultipleEndpoints_RegistersAll()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("llm");
        services.AddBaizeOpenAiGeneration("ep-1", options => options.Model = "gpt-image-1");
        services.AddBaizeOpenAiGeneration("ep-2", options => options.Model = "gpt-image-2");

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredKeyedService<IGenerationClient>("ep-1");
        var second = provider.GetRequiredKeyedService<IGenerationClient>("ep-2");

        first.Should().NotBeSameAs(second);
        provider.GetRequiredService<IGenerationClientRegistry>().Find("OpenAi", "ep-1").Should().BeSameAs(first);
        provider.GetRequiredService<IGenerationClientRegistry>().Find("OpenAi", "ep-2").Should().BeSameAs(second);
    }

    [Fact]
    public async Task AddBaizeOpenAiCompatibleGeneration_OptsInOnlyConfiguredFeatures()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("llm");
        services.AddBaizeOpenAiCompatibleGeneration("local-image", options =>
        {
            options.BaseAddress = new Uri("http://localhost:8000/v1");
            options.Model = "image-model";
            options.Features = GenerationFeature.TextToImage;
        });

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<IGenerationClient>("local-image");
        client.Capabilities.Features.Should().Be(GenerationFeature.TextToImage);

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "clip" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
    }

    private static OpenAiGenerationClient CreateClient(
        RecordingHandler handler,
        bool video = false,
        bool audio = false) =>
        new(
            model: "gpt-image-1",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            new Uri("https://openai.test/v1"),
            new GenerationCapabilities
            {
                Features =
                    GenerationFeature.TextToImage |
                    GenerationFeature.ImageToImage |
                    GenerationFeature.TextToVideo |
                    GenerationFeature.TextToSpeech |
                    GenerationFeature.MultipleCandidates |
                    GenerationFeature.OperationRetrieval |
                    (video ? GenerationFeature.Progress : GenerationFeature.None),
                InputTransports = new HashSet<LlmContentTransport>
                {
                    LlmContentTransport.Uri,
                    LlmContentTransport.InlineData
                }
            },
            endpointId: "openai-gen-1",
            imageModel: "gpt-image-1",
            videoModel: "gpt-video-1",
            audioModel: "tts-1",
            defaultVoice: "alloy");
}