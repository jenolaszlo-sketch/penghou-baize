using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Runway.Tests;

public sealed class RunwayGenerationClientTests
{
    [Fact]
    public async Task SubmitAsync_TextToVideo_SendsTextToVideoAndReturnsQueuedOperation()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"task-1","estimatedCost":{"credits":40.0}}""");
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "a dog running through a field",
                AspectRatio = "16:9",
                Duration = TimeSpan.FromSeconds(5),
                Seed = 42
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Queued);
        operation.Result.Should().BeNull();
        operation.Handle.Provider.Should().Be("Runway");
        operation.Handle.EndpointId.Should().Be("runway-gen-1");
        operation.Handle.Id.Should().Be("task-1");
        operation.ProviderMetadata.Should().ContainKey("estimated_cost_credits");

        handler.Requests.Should().HaveCount(1);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/text_to_video");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.Headers.Authorization.Should().Be(
            new AuthenticationHeaderValue("Bearer", "secret"));
        handler.LastRequest!.Headers.Should().Contain(header =>
            header.Key == "X-Runway-Version" && header.Value.First() == "2024-11-06");

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("model").GetString().Should().Be("gen4.5");
        root.GetProperty("promptText").GetString().Should().Be("a dog running through a field");
        root.GetProperty("ratio").GetString().Should().Be("16:9");
        root.GetProperty("duration").GetInt32().Should().Be(5);
        root.GetProperty("seed").GetInt32().Should().Be(42);
        root.TryGetProperty("promptImage", out _).Should().BeFalse();
    }

    // Regression: unsupported inputs must fail fast instead of silently
    // degrading to text/image-to-video and billing the wrong generation.

    [Fact]
    public async Task SubmitAsync_VideoToVideo_FailsFastAsUnsupported()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-x"}""");
        var client = CreateClient(handler);

        var action = () => client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "restyle",
                SourceVideo = new LlmUriSource(new Uri("https://cdn.test/in.mp4"))
            },
            TestContext.Current.CancellationToken);

        var exception = (await action.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_LastFrame_FailsFastAsUnsupported()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-x"}""");
        var client = CreateClient(handler);

        var action = () => client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "interpolate",
                LastFrame = new LlmUriSource(new Uri("https://cdn.test/last.png"))
            },
            TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<BaizeException>())
            .Which.ErrorKind.Should().Be(GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_References_FailFastAsUnsupported()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-x"}""");
        var client = CreateClient(handler);

        var action = () => client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "conditioned",
                References = [new LlmUriSource(new Uri("https://cdn.test/ref.png"))]
            },
            TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<BaizeException>())
            .Which.ErrorKind.Should().Be(GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_IdempotencyKey_FailsFastAsUnsupported()
    {
        // Runway's API exposes no idempotent submission; asserting a key must
        // surface instead of being silently ignored.
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-x"}""");
        var client = CreateClient(handler);

        var action = () => client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "a dog", IdempotencyKey = "order-42" },
            TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<BaizeException>())
            .Which.ErrorKind.Should().Be(GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_TextToVideo_UsesConfiguredDefaultsWhenOmitted()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-1"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "waves" },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("ratio").GetString().Should().Be("1280:720");
        root.TryGetProperty("duration", out _).Should().BeFalse();
        root.TryGetProperty("seed", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_ImageToVideo_WithUriFirstFrame_SendsPromptImage()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-2"}""");
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "animate the first frame",
                FirstFrame = new LlmUriSource(new Uri("https://cdn.test/first.png"))
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Queued);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/image_to_video");

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("model").GetString().Should().Be("gen4.5");
        root.GetProperty("promptImage").GetString().Should().Be("https://cdn.test/first.png");
        root.GetProperty("promptText").GetString().Should().Be("animate the first frame");
    }

    [Fact]
    public async Task SubmitAsync_ImageToVideo_WithInlineFirstFrame_EncodesDataUri()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-3"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "animate",
                FirstFrame = new LlmInlineDataSource(new byte[] { 1, 2, 3 })
            },
            TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/image_to_video");
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("promptImage").GetString().Should().Be("data:image/png;base64,AQID");
    }

    [Fact]
    public async Task SubmitAsync_ImageRequest_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "an icon" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_AudioRequest_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new AudioGenerationRequest { Prompt = "speech" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(401, GenerationErrorKind.Authentication)]
    [InlineData(403, GenerationErrorKind.Authorization)]
    [InlineData(429, GenerationErrorKind.RateLimited)]
    public async Task SubmitAsync_HttpFailure_ClassifiesCorrectly(
        int statusCode,
        GenerationErrorKind expectedKind)
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"error":"boom"}""", statusCode);
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "clip" },
            TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<BaizeException>();
        exception.Which.ErrorKind.Should().Be(expectedKind);
        exception.Which.StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public async Task SubmitAsync_ConnectionFailure_ReportsUnknownSubmissionOutcome()
    {
        var handler = new RecordingHandler().ThrowOnSend(new HttpRequestException("connection reset"));
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "clip" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnknownSubmissionOutcome);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SubmitAsync_MalformedSuccessResponse_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler().ReturnJson("this is not valid json {");
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "clip" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task SubmitAsync_ResponseWithoutTaskId_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler().ReturnJson("""{"estimatedCost":{"credits":10}}""");
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "clip" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task SubmitAsync_CanceledToken_PropagatesCancellation()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "clip" },
            cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetAsync_QueuedStatus_MapsToQueued()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"task-1","status":"PENDING","createdAt":"2026-01-01T00:00:00Z"}""");
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Queued);
        operation.Progress.Should().BeNull();
        operation.ProviderMetadata.Should().Contain("status", "PENDING");
    }

    [Fact]
    public async Task GetAsync_RunningStatus_MapsProgress()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"task-1","status":"RUNNING","progress":0.6}""");
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Running);
        operation.Progress.Should().Be(0.6);
    }

    [Fact]
    public async Task GetAsync_SucceededStatus_MapsOutputAssets()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"task-1","status":"SUCCEEDED","output":["https://cdn.test/v1.mp4","https://cdn.test/v2.mp4"],"cost":{"credits":40.0}}""");
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result!.Assets.Should().HaveCount(2);
        operation.Result.Assets[0].Source.As<UriGeneratedAssetSource>().Uri.ToString().Should().Be("https://cdn.test/v1.mp4");
        operation.Result.Assets[0].ContentType.Should().Be("video/mp4");
        operation.ProviderMetadata.Should().Contain("cost_credits", 40.0);
    }

    [Fact]
    public async Task GetAsync_SucceededWithoutOutput_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-1","status":"SUCCEEDED"}""");
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var action = async () => await client.GetAsync(handle, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task GetAsync_FailedStatus_MapsFailureWithCode()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"task-1","status":"FAILED","failure":"content policy","failureCode":"SAFETY_REJECTED"}""");
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error.Should().NotBeNull();
        operation.Error!.Kind.Should().Be(GenerationErrorKind.GenerationFailed);
        operation.Error.Message.Should().Be("content policy");
        operation.Error.ProviderStatus.Should().Be("SAFETY_REJECTED");
    }

    [Fact]
    public async Task GetAsync_CancelledStatus_MapsToCanceled()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-1","status":"CANCELLED"}""");
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Canceled);
    }

    [Fact]
    public async Task GetAsync_UnknownStatus_RemainsUnknown()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-1","status":"QUARANTINED"}""");
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Unknown);
    }

    [Fact]
    public async Task GetAsync_UsesTaskIdFromHandle()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-9","status":"RUNNING"}""");
        var client = CreateClient(handler);
        var handle = Handle("task-9");

        await client.GetAsync(handle, TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/tasks/task-9");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task GetAsync_ConnectionFailure_ReportsProviderUnavailable()
    {
        var handler = new RecordingHandler().ThrowOnSend(new HttpRequestException("provider down"));
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var action = async () => await client.GetAsync(handle, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.ProviderUnavailable);
    }

    [Fact]
    public async Task CancelAsync_DeletesTaskAndReturnsCanceled()
    {
        var handler = new RecordingHandler().ReturnEmpty();
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var operation = await client.CancelAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Canceled);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/tasks/task-1");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task CancelAsync_HttpFailure_Classifies()
    {
        var handler = new RecordingHandler().ReturnJson("""{"error":"forbidden"}""", 403);
        var client = CreateClient(handler);
        var handle = Handle("task-1");

        var action = async () => await client.CancelAsync(handle, TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<BaizeException>();
        exception.Which.ErrorKind.Should().Be(GenerationErrorKind.Authorization);
    }

    [Fact]
    public async Task GetAsync_ForeignHandle_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);
        var foreign = new GenerationOperationHandle("OpenAi", "other-endpoint", "op-1");

        var action = async () => await client.GetAsync(foreign, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.InvalidRequest);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelAsync_ForeignHandle_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);
        var foreign = new GenerationOperationHandle("Runway", "other-endpoint", "op-1");

        var action = async () => await client.CancelAsync(foreign, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.InvalidRequest);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Capabilities_ExposeConfiguredFeatures()
    {
        var client = CreateClient(new RecordingHandler());

        client.Capabilities.Features.Should().Be(
            GenerationFeature.TextToVideo |
            GenerationFeature.ImageToVideo |
            GenerationFeature.OperationRetrieval |
            GenerationFeature.Cancellation |
            GenerationFeature.Progress);
        client.Capabilities.InputTransports.Should().BeEquivalentTo(
            new[] { LlmContentTransport.Uri, LlmContentTransport.InlineData, LlmContentTransport.ProviderFile });
    }

    [Fact]
    public async Task SubmitAsync_ImageToVideo_WithRunwayHostedFile_SendsFileId()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-4"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "animate",
                FirstFrame = new LlmProviderFileSource(new LlmProviderKey("Runway"), "runway://file-1")
            },
            TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/image_to_video");
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("promptImage").GetString().Should().Be("runway://file-1");
    }

    [Fact]
    public async Task SubmitAsync_ImageToVideo_WithForeignProviderFile_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "animate",
                FirstFrame = new LlmProviderFileSource(new LlmProviderKey("Gemini"), "file-1")
            },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateEphemeralUploadAsync_ReservesUploadSlot()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"up-1","uploadUrl":"https://storage.test/upload","runwayUri":"runway://up-1","fields":{"key":"abc","policy":"xyz"}}""");
        var client = CreateClient(handler);

        var upload = await client.CreateEphemeralUploadAsync(
            "first-frame.png",
            TestContext.Current.CancellationToken);

        upload.Id.Should().Be("up-1");
        upload.UploadUrl.Should().Be("https://storage.test/upload");
        upload.RunwayUri.Should().Be("runway://up-1");
        upload.Fields.Should().Contain("key", "abc");

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/uploads");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("filename").GetString().Should().Be("first-frame.png");
        body.RootElement.GetProperty("type").GetString().Should().Be("ephemeral");
    }

    [Fact]
    public async Task UploadFileAsync_PostsMultipartToPresignedUrl_ReturnsRunwayUri()
    {
        var handler = new RecordingHandler().ReturnEmpty();
        var client = CreateClient(handler);

        var runwayUri = await client.UploadFileAsync(
            new RunwayUploadCreateResponse
            {
                Id = "up-1",
                UploadUrl = "https://storage.test/upload",
                RunwayUri = "runway://up-1",
                Fields = new Dictionary<string, string> { ["key"] = "abc", ["policy"] = "xyz" }
            },
            new byte[] { 1, 2, 3 },
            "first-frame.png",
            "image/png",
            TestContext.Current.CancellationToken);

        runwayUri.Should().Be("runway://up-1");
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://storage.test/upload");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequestBody!.Should().Contain("name=key").And.Contain("abc");
        handler.LastRequestBody!.Should().Contain("name=file").And.Contain("first-frame.png");
        handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task UploadFileAsync_ReservationWithoutUrl_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var action = async () => await client.UploadFileAsync(
            new RunwayUploadCreateResponse { Id = "up-1" },
            new byte[] { 1, 2, 3 },
            "first-frame.png",
            "image/png",
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.GenerationFailed);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateTextToVideoAsync_NativeMethod_PostsProviderPayload()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-1","estimatedCost":{"credits":40}}""");
        var client = CreateClient(handler);

        var response = await client.CreateTextToVideoAsync(
            new RunwayTextToVideoRequest
            {
                Model = "gen4.5",
                PromptText = "waves on a shore",
                Ratio = "1280:720",
                Duration = 10,
                Seed = 7,
                OutputFormat = "mp4",
                Audio = true,
                NegativePrompt = "blur"
            },
            TestContext.Current.CancellationToken);

        response.Id.Should().Be("task-1");
        response.EstimatedCost!.Credits.Should().Be(40);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("audio").GetBoolean().Should().BeTrue();
        root.GetProperty("negativePrompt").GetString().Should().Be("blur");
        root.GetProperty("outputFormat").GetString().Should().Be("mp4");
    }

    [Fact]
    public async Task CreateImageToVideoAsync_NativeMethod_PostsProviderPayload()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-2"}""");
        var client = CreateClient(handler);

        var response = await client.CreateImageToVideoAsync(
            new RunwayImageToVideoRequest
            {
                Model = "gen4.5",
                PromptImage = "https://cdn.test/first.png",
                PromptText = "animate"
            },
            TestContext.Current.CancellationToken);

        response.Id.Should().Be("task-2");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/image_to_video");
    }

    [Fact]
    public async Task GetTaskAsync_NativeMethod_ReturnsTaskSnapshot()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"id":"task-1","status":"RUNNING","progress":0.3}""");
        var client = CreateClient(handler);

        var task = await client.GetTaskAsync("task-1", TestContext.Current.CancellationToken);

        task.Id.Should().Be("task-1");
        task.Status.Should().Be("RUNNING");
        task.Progress.Should().Be(0.3);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/tasks/task-1");
    }

    [Fact]
    public async Task CancelTaskAsync_NativeMethod_SendsDelete()
    {
        var handler = new RecordingHandler().ReturnEmpty();
        var client = CreateClient(handler);

        await client.CancelTaskAsync("task-1", TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/tasks/task-1");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public void AddBaizeRunwayGeneration_RegistersKeyedClientAndRegistryEntry()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddBaizeGeneration();
        services.AddBaizeRunwayGeneration("runway-gen-1", options =>
        {
            options.ApiKey = "secret";
            options.Model = "gen4.5";
        });
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredKeyedService<IGenerationClient>("runway-gen-1");
        client.Should().BeOfType<RunwayGenerationClient>();
        client.Capabilities.Features.Should().HaveFlag(GenerationFeature.TextToVideo);
        client.Capabilities.InputTransports.Should().Contain(LlmContentTransport.ProviderFile);

        var registry = provider.GetRequiredService<IGenerationClientRegistry>();
        registry.Find("Runway", "runway-gen-1").Should().NotBeNull();
        registry.Find("Runway", "runway-gen-1")!.Capabilities.Features.Should().HaveFlag(GenerationFeature.TextToVideo);
    }

    private static GenerationOperationHandle Handle(string id) =>
        new("Runway", "runway-gen-1", id, "gen4.5");

    private static RunwayGenerationClient CreateClient(
        RecordingHandler handler,
        GenerationFeature features =
            GenerationFeature.TextToVideo |
            GenerationFeature.ImageToVideo |
            GenerationFeature.OperationRetrieval |
            GenerationFeature.Cancellation |
            GenerationFeature.Progress) =>
        new(
            model: "gen4.5",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            new Uri("https://api.dev.runwayml.com/v1"),
            new GenerationCapabilities
            {
                Features = features,
                InputTransports = new HashSet<LlmContentTransport>
                {
                    LlmContentTransport.Uri,
                    LlmContentTransport.InlineData,
                    LlmContentTransport.ProviderFile
                }
            },
            "runway-gen-1",
            apiVersion: "2024-11-06",
            defaultInputImageMimeType: "image/png",
            defaultRatio: "1280:720");
}