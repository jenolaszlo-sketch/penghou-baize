using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Baize.Fal.Tests;

public sealed class FalGenerationClientTests
{
    [Fact]
    public async Task SubmitAsync_Image_SendsQueuePayloadAndReturnsQueuedOperation()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"IN_QUEUE","cancel_url":"https://queue.fal.run/requests/r-1/cancel"}""");
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "a lighthouse", Seed = 42 },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Queued);
        operation.Result.Should().BeNull();
        operation.Handle.Provider.Should().Be("Fal");
        operation.Handle.EndpointId.Should().Be("fal-gen-1");
        operation.Handle.Id.Should().Be("r-1");
        operation.Handle.Model.Should().Be("fal-ai/flux/dev");

        handler.Requests.Should().HaveCount(1);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/fal-ai/flux/dev");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.Headers.Authorization.Should().Be(
            new AuthenticationHeaderValue("Key", "secret"));

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("prompt").GetString().Should().Be("a lighthouse");
        root.GetProperty("seed").GetInt32().Should().Be(42);
        root.TryGetProperty("image_url", out _).Should().BeFalse();
        root.TryGetProperty("num_images", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PersistedHandle_UsesProviderIssuedStatusAndResponseUrls()
    {
        var handler = new RecordingHandler()
            .ReturnJson(
                """{"request_id":"r-links","status":"IN_QUEUE","status_url":"https://status.example/custom","response_url":"https://result.example/custom","cancel_url":"https://cancel.example/custom"}""")
            .ReturnJson("""{"request_id":"r-links","status":"COMPLETED"}""")
            .ReturnJson("""{"image":{"url":"https://cdn.example/result.png"}}""");
        var client = CreateClient(handler);

        var submitted = await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "linked" },
            TestContext.Current.CancellationToken);
        var resumedHandle = submitted.Handle with { };

        var completed = await client.GetAsync(
            resumedHandle,
            TestContext.Current.CancellationToken);

        completed.State.Should().Be(GenerationOperationState.Succeeded);
        handler.Requests[1].RequestUri.Should().Be("https://status.example/custom");
        handler.Requests[2].RequestUri.Should().Be("https://result.example/custom");
        resumedHandle.ProviderData.Should().Contain("cancel_url", "https://cancel.example/custom");
    }

    [Fact]
    public async Task SubmitAsync_ImageWithCountAndCandidates_SendsNumImages()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-2"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "variations", Count = 3 },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("num_images").GetInt32().Should().Be(3);
    }

    // Regression: validated request fields must reach the payload instead of
    // being silently dropped (a caller sets Size, validation passes, and a
    // wrong-sized asset gets billed).

    [Fact]
    public async Task SubmitAsync_ImageOptionalFields_AreMappedToPayload()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-opt"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt = "poster",
                AspectRatio = "16:9",
                Size = new GenerationImageSize(1920, 1080),
                OutputFormat = "jpeg",
                Inputs =
                [
                    new LlmUriSource(new Uri("https://cdn.test/r1.png")),
                    new LlmUriSource(new Uri("https://cdn.test/r2.png"))
                ]
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("aspect_ratio").GetString().Should().Be("16:9");
        root.GetProperty("image_size").GetProperty("width").GetInt32().Should().Be(1920);
        root.GetProperty("image_size").GetProperty("height").GetInt32().Should().Be(1080);
        root.GetProperty("output_format").GetString().Should().Be("jpeg");
        var references = root.GetProperty("reference_image_urls");
        references.GetArrayLength().Should().Be(1);
        references[0].GetString().Should().Be("https://cdn.test/r2.png");
    }

    [Fact]
    public async Task SubmitAsync_VideoOptionalFields_AreMappedToPayload()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-vid"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "drift through clouds",
                Duration = TimeSpan.FromSeconds(6),
                GenerateAudio = true,
                LastFrame = new LlmUriSource(new Uri("https://cdn.test/last.png"))
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("duration").GetInt32().Should().Be(6);
        root.GetProperty("generate_audio").GetBoolean().Should().BeTrue();
        root.GetProperty("last_image_url").GetString().Should().Be("https://cdn.test/last.png");
    }

    [Fact]
    public async Task SubmitAsync_AudioOutputFormat_MimeFormIsNormalized()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-aud"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new AudioGenerationRequest
            {
                Prompt = "lo-fi loop",
                OutputFormat = "audio/wav",
                Duration = TimeSpan.FromSeconds(30)
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("output_format").GetString().Should().Be("wav");
        body.RootElement.GetProperty("duration").GetInt32().Should().Be(30);
    }

    // Regression: IdempotencyKey was advertised on GenerationRequest but never
    // sent, leaving replay-after-ambiguity unprotected.

    [Fact]
    public async Task SubmitAsync_IdempotencyKey_IsForwardedAsFalHeader()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-idem"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "one of a kind", IdempotencyKey = "order-42" },
            TestContext.Current.CancellationToken);

        handler.LastRequest!.Headers.TryGetValues("x-fal-idempotency-key", out var values)
            .Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be("order-42");
    }

    [Fact]
    public async Task SubmitAsync_ImageToImage_WithUriInput_SendsImageUrl()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-3"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt = "make it rainy",
                Inputs = [new LlmUriSource(new Uri("https://cdn.test/scene.png"))]
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("image_url").GetString().Should().Be("https://cdn.test/scene.png");
    }

    [Fact]
    public async Task SubmitAsync_ImageToImage_WithInlineInput_EncodesDataUri()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-4"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt = "restyle",
                Inputs = [new LlmInlineDataSource(new byte[] { 1, 2, 3 })]
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("image_url").GetString().Should().Be("data:image/png;base64,AQID");
    }

    [Fact]
    public async Task SubmitAsync_VideoTextToVideo_SendsPrompt()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-5"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "cloud timelapse" },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("prompt").GetString().Should().Be("cloud timelapse");
        body.RootElement.TryGetProperty("image_url", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("input_video", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_ImageToVideo_WithFirstFrame_SendsImageUrl()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-6"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "animate",
                FirstFrame = new LlmUriSource(new Uri("https://cdn.test/first.png"))
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("image_url").GetString().Should().Be("https://cdn.test/first.png");
        body.RootElement.TryGetProperty("input_video", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_VideoToVideo_WithSource_SendsInputVideo()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-7"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt = "stylize",
                SourceVideo = new LlmUriSource(new Uri("https://cdn.test/source.mp4")),
                Seed = 7
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("input_video").GetString().Should().Be("https://cdn.test/source.mp4");
        body.RootElement.GetProperty("seed").GetInt32().Should().Be(7);
        body.RootElement.TryGetProperty("image_url", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_AudioSpeech_SendsPromptAndVoice()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-8"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new AudioGenerationRequest { Prompt = "hello", Voice = "calmita" },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("prompt").GetString().Should().Be("hello");
        body.RootElement.GetProperty("voice").GetString().Should().Be("calmita");
        body.RootElement.TryGetProperty("input_audio", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_AudioTransform_WithSource_SendsInputAudio()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-9"}""");
        var client = CreateClient(handler);

        await client.SubmitAsync(
            new AudioGenerationRequest
            {
                Prompt = "remove background noise",
                Kind = AudioGenerationKind.Transform,
                SourceAudio = new LlmUriSource(new Uri("https://cdn.test/narr.wav"))
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("input_audio").GetString().Should().Be("https://cdn.test/narr.wav");
        body.RootElement.TryGetProperty("voice", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_ProviderFileInput_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt = "edit",
                Inputs = [new LlmProviderFileSource(new LlmProviderKey("Runway"), "file-1")]
            },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_WithoutOperationRetrieval_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(
            handler,
            GenerationFeature.TextToImage | GenerationFeature.Cancellation);
        var handle = Handle("r-1");

        var action = async () => await client.GetAsync(handle, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelAsync_WithoutCancellation_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(
            handler,
            GenerationFeature.TextToImage | GenerationFeature.OperationRetrieval);
        var handle = Handle("r-1");

        var action = async () => await client.CancelAsync(handle, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_UnsupportedVideoRequest_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(
            handler,
            GenerationFeature.TextToImage |
            GenerationFeature.OperationRetrieval |
            GenerationFeature.Cancellation);

        var action = async () => await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "clip" },
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
            """{"detail":"boom"}""", statusCode);
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "an icon" },
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
            new ImageGenerationRequest { Prompt = "an icon" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnknownSubmissionOutcome);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SubmitAsync_MalformedSuccessResponse_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler().ReturnJson("not json {");
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "an icon" },
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task SubmitAsync_ResponseWithoutRequestId_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler().ReturnJson("""{"status":"IN_QUEUE"}""");
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            new ImageGenerationRequest { Prompt = "an icon" },
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
            new ImageGenerationRequest { Prompt = "an icon" },
            cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetAsync_InQueue_MapsToQueuedWithQueuePosition()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"IN_QUEUE","position":3}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Queued);
        operation.Progress.Should().BeNull();
        operation.ProviderMetadata.Should().Contain("status", "IN_QUEUE");
        operation.ProviderMetadata.Should().Contain("queue_position", 3);
        operation.ProviderMetadata.Should().Contain("provider_id", "r-1");
    }

    [Fact]
    public async Task GetAsync_InProgress_MapsRunningWithMetricsAndNoProgress()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"IN_PROGRESS","metrics":{"queue_time":2.1,"inference_time":4.5,"total_time":6.6}}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Running);
        operation.Progress.Should().BeNull();
        operation.ProviderMetadata.Should().NotContainKey("queue_position");
        operation.ProviderMetadata.Should().Contain("queue_time", 2.1);
        operation.ProviderMetadata.Should().Contain("inference_time", 4.5);
        operation.ProviderMetadata.Should().Contain("total_time", 6.6);
    }

    [Fact]
    public async Task GetAsync_Completed_FetchesResultAndExtractsArbitraryAssets()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson(
                """{"images":[{"url":"https://v3.fal.media/files/abc.png"}],"video":{"url":"https://v3.fal.media/files/out.mp4"},"seed":42}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result!.Assets.Should().HaveCount(2);
        operation.Result.Assets[0].Source.As<UriGeneratedAssetSource>().Uri.ToString()
            .Should().Be("https://v3.fal.media/files/abc.png");
        operation.Result.Assets[0].ContentType.Should().Be("image/png");
        operation.Result.Assets[1].ContentType.Should().Be("video/mp4");
        operation.Result.Metadata.Should().ContainKey("raw_output");

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/requests/r-1/status");
        handler.Requests[1].RequestUri!.AbsolutePath.Should().Be("/requests/r-1");
    }

    [Fact]
    public async Task GetAsync_CompletedWithErrorDocument_MapsToFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"status":"ERROR","detail":"content moderation"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error.Should().NotBeNull();
        operation.Error!.Kind.Should().Be(GenerationErrorKind.GenerationFailed);
        operation.Error.Message.Should().Be("content moderation");
    }

    [Fact]
    public async Task GetAsync_CompletedWithoutOutput_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"seed":42}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var action = async () => await client.GetAsync(handle, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task GetAsync_ErrorStatus_MapsToFailed()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"ERROR"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error!.Kind.Should().Be(GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task GetAsync_CanceledStatus_MapsToCanceled()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"CANCELED"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Canceled);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAsync_UnknownStatus_RemainsUnknown()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"QUARANTINED"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Unknown);
    }

    [Fact]
    public async Task GetAsync_UsesRequestIdFromHandle()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-9","status":"IN_PROGRESS"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-9");

        await client.GetAsync(handle, TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/requests/r-9/status");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task GetAsync_ConnectionFailure_ReportsProviderUnavailable()
    {
        var handler = new RecordingHandler().ThrowOnSend(new HttpRequestException("provider down"));
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var action = async () => await client.GetAsync(handle, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.ProviderUnavailable);
    }

    [Fact]
    public async Task CancelAsync_PutsCancelAndReturnsCanceled()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"CANCELED"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.CancelAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Canceled);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/requests/r-1/cancel");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Put);
    }

    [Fact]
    public async Task CancelAsync_HttpFailure_Classifies()
    {
        var handler = new RecordingHandler().ReturnJson("""{"detail":"forbidden"}""", 403);
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var action = async () => await client.CancelAsync(handle, TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<BaizeException>();
        exception.Which.ErrorKind.Should().Be(GenerationErrorKind.Authorization);
    }

    [Fact]
    public async Task GetAsync_ForeignHandle_RejectedBeforeProviderCall()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);
        var foreign = new GenerationOperationHandle("Runway", "other-endpoint", "op-1");

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
        var foreign = new GenerationOperationHandle("Fal", "other-endpoint", "op-1");

        var action = async () => await client.CancelAsync(foreign, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.InvalidRequest);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitQueueAsync_NativeMethod_PostsPayloadUnchanged()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"IN_QUEUE"}""");
        var client = CreateClient(handler);

        var payload = new JsonObject
        {
            ["fps"] = 24,
            ["enable_safety_checker"] = true
        };
        var response = await client.SubmitQueueAsync(
            payload,
            cancellationToken: TestContext.Current.CancellationToken);

        response.RequestId.Should().Be("r-1");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/fal-ai/flux/dev");
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("fps").GetInt32().Should().Be(24);
        body.RootElement.GetProperty("enable_safety_checker").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_NativeMethod_ReturnsSnapshot()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"request_id":"r-1","status":"IN_PROGRESS"}""");
        var client = CreateClient(handler);

        var status = await client.GetStatusAsync("r-1", TestContext.Current.CancellationToken);

        status.Status.Should().Be("IN_PROGRESS");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/requests/r-1/status");
    }

    [Fact]
    public async Task GetResultAsync_NativeMethod_ReturnsDocument()
    {
        var handler = new RecordingHandler().ReturnJson(
            """{"images":[{"url":"https://v3.fal.media/files/a.png"}]}""");
        var client = CreateClient(handler);

        var result = await client.GetResultAsync("r-1", TestContext.Current.CancellationToken);

        result.GetProperty("images")[0].GetProperty("url").GetString()
            .Should().Be("https://v3.fal.media/files/a.png");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/requests/r-1");
    }

    [Fact]
    public async Task CancelQueueAsync_NativeMethod_SendsPut()
    {
        var handler = new RecordingHandler().ReturnJson("""{"request_id":"r-1","status":"CANCELED"}""");
        var client = CreateClient(handler);

        await client.CancelQueueAsync("r-1", TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/requests/r-1/cancel");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Put);
    }

    [Fact]
    public void Capabilities_ExposeConfiguredFeatures()
    {
        var client = CreateClient(new RecordingHandler());

        client.Capabilities.Features.Should().HaveFlag(GenerationFeature.TextToImage);
        client.Capabilities.Features.Should().HaveFlag(GenerationFeature.OperationRetrieval);
        client.Capabilities.InputTransports.Should().BeEquivalentTo(
            new[] { LlmContentTransport.Uri, LlmContentTransport.InlineData });
    }

    [Fact]
    public void AddBaizeFalGeneration_RegistersKeyedClientAndRegistryEntry()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddBaizeGeneration();
        services.AddBaizeFalGeneration("fal-gen-1", options =>
        {
            options.ApiKey = "secret";
            options.Model = "fal-ai/flux/dev";
        });
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredKeyedService<IGenerationClient>("fal-gen-1");
        client.Should().BeOfType<FalGenerationClient>();
        client.Capabilities.Features.Should().HaveFlag(GenerationFeature.TextToImage);

        var registry = provider.GetRequiredService<IGenerationClientRegistry>();
        registry.Find("Fal", "fal-gen-1").Should().NotBeNull();
        registry.Find("Fal", "fal-gen-1")!.Capabilities.Features.Should().HaveFlag(GenerationFeature.OperationRetrieval);
    }

    [Fact]
    public async Task GetAsync_CompletedWithErrorStatusAndMessage_MapsToFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"status":"ERROR","message":"policy block"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error!.Message.Should().Be("policy block");
    }

    [Fact]
    public async Task GetAsync_CompletedWithErrorStatusWithoutMessage_MapsToFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"status":"ERROR"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error!.Message.Should().Be("fal request completed with an ERROR status.");
    }

    [Fact]
    public async Task GetAsync_CompletedWithTopLevelDetail_MapsToFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"detail":"quota exhausted"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error!.Message.Should().Be("quota exhausted");
    }

    [Fact]
    public async Task GetAsync_CompletedWithStringError_MapsToFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"error":"model crashed"}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error!.Message.Should().Be("model crashed");
    }

    [Fact]
    public async Task GetAsync_CompletedWithErrorObjectDetail_MapsToFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"error":{"detail":"schema rejected"}}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error!.Message.Should().Be("schema rejected");
    }

    [Fact]
    public async Task GetAsync_CompletedWithErrorObjectMessage_MapsToFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"error":{"message":"safety gate"}}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error!.Message.Should().Be("safety gate");
    }

    [Fact]
    public async Task GetAsync_CompletedWithErrorObjectWithoutMessage_MapsToFailed()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""{"error":{"code":123}}""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error!.Message.Should().Be("fal request reported an error document.");
    }

    [Fact]
    public async Task GetAsync_CompletedWithArrayResult_ExtractsAssets()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson("""[{"url":"https://v3.fal.media/files/a.png"}]""");
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result!.Assets.Should().ContainSingle()
            .Which.Source.As<UriGeneratedAssetSource>().Uri.ToString()
            .Should().Be("https://v3.fal.media/files/a.png");
    }

    [Fact]
    public async Task GetAsync_Completed_InfersContentTypesAcrossExtensions()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson(
                """
                {
                  "images":[
                    {"url":"https://cdn.test/a.png"},
                    {"url":"https://cdn.test/b.jpg"},
                    {"url":"https://cdn.test/c.jpeg"},
                    {"url":"https://cdn.test/d.webp"},
                    {"url":"https://cdn.test/e.gif"}
                  ],
                  "video":{"url":"https://cdn.test/v.mov"},
                  "animated":{"url":"https://cdn.test/w.webm"},
                  "audio":[
                    {"url":"https://cdn.test/a.mp3"},
                    {"url":"https://cdn.test/b.wav"},
                    {"url":"https://cdn.test/c.m4a"},
                    {"url":"https://cdn.test/d.flac"}
                  ],
                  "files":[{"url":"https://cdn.test/raw.bin"},{"url":"https://cdn.test/noext"}],
                  "ftp":"ftp://cdn.test/ignored.png",
                  "relative":"files/also-ignored.png"
                }
                """);
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result!.Assets.Select(asset => asset.ContentType).Should().BeEquivalentTo(
            [
                "image/png", "image/jpeg", "image/jpeg", "image/webp", "image/gif",
                "video/quicktime", "video/webm",
                "audio/mpeg", "audio/wav", "audio/mp4", "audio/flac",
                null, null
            ]);
        operation.Result.Assets.Should().HaveCount(13);
    }

    [Fact]
    public async Task GetAsync_Completed_PreservesDocumentedAssetMetadata()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson(
                """
                {
                  "images":[
                    {
                      "url":"https://v3.fal.media/files/abc123/output.png",
                      "content_type":"image/png",
                      "file_name":"output.png",
                      "file_size":48291
                    },
                    {
                      "url":"https://v3.fal.media/files/def456/raw.bin",
                      "file_name":"raw.bin"
                    }
                  ],
                  "video":{
                    "url":"https://v3.fal.media/files/ghi789/clip.mp4",
                    "content_type":"video/mp4",
                    "file_size":1048576
                  },
                  "audio":"https://v3.fal.media/files/jkl012/voice.wav"
                }
                """);
        var client = CreateClient(handler);
        var handle = Handle("r-1");

        var operation = await client.GetAsync(handle, TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result!.Assets.Should().HaveCount(4);

        var image = operation.Result.Assets[0];
        image.ContentType.Should().Be("image/png");
        image.FileName.Should().Be("output.png");
        image.Size.Should().Be(48291);

        var raw = operation.Result.Assets[1];
        raw.ContentType.Should().BeNull();
        raw.FileName.Should().Be("raw.bin");

        var video = operation.Result.Assets[2];
        video.ContentType.Should().Be("video/mp4");
        video.Size.Should().Be(1048576);

        var bareAudio = operation.Result.Assets[3];
        bareAudio.Source.As<UriGeneratedAssetSource>().Uri.ToString()
            .Should().Be("https://v3.fal.media/files/jkl012/voice.wav");
        bareAudio.ContentType.Should().Be("audio/wav");
    }

    private static GenerationOperationHandle Handle(string id) =>
        new("Fal", "fal-gen-1", id, "fal-ai/flux/dev");

    private static FalGenerationClient CreateClient(
        RecordingHandler handler,
        GenerationFeature features =
            GenerationFeature.TextToImage |
            GenerationFeature.ImageToImage |
            GenerationFeature.TextToVideo |
            GenerationFeature.ImageToVideo |
            GenerationFeature.VideoToVideo |
            GenerationFeature.TextToSpeech |
            GenerationFeature.TextToSound |
            GenerationFeature.AudioTransform |
            GenerationFeature.MultipleCandidates |
            GenerationFeature.OperationRetrieval |
            GenerationFeature.Cancellation) =>
        new(
            model: "fal-ai/flux/dev",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            baseUrl: "https://queue.fal.run",
            new GenerationCapabilities
            {
                Features = features,
                InputTransports = new HashSet<LlmContentTransport>
                {
                    LlmContentTransport.Uri,
                    LlmContentTransport.InlineData
                }
            },
            "fal-gen-1");
}
