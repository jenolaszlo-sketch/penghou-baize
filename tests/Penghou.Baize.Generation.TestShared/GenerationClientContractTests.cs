using FluentAssertions;
using Penghou.Baize.Generation;
using Xunit;

namespace Penghou.Baize.Generation.TestShared;

/// <summary>
/// Shared conformance suite for <see cref="IGenerationClient"/> providers.
/// Subclasses provide provider-specific hooks (capabilities, request building,
/// wire payloads) and every provider must pass these tests deterministically
/// against recorded payloads.
/// </summary>
public abstract class GenerationClientContractTests
{
    /// <summary>The provider name expected on operation handles.</summary>
    protected abstract string ProviderName { get; }

    /// <summary>The configured endpoint identity expected on operation handles.</summary>
    protected abstract string EndpointId { get; }

    /// <summary>The capabilities the conformance fixture configures (image-only).</summary>
    protected abstract GenerationCapabilities ImageOnlyCapabilities { get; }

    /// <summary>Builds a client backed by the recording handler.</summary>
    protected abstract IGenerationClient CreateClient(RecordingHandler handler);

    /// <summary>Builds an image request.</summary>
    protected abstract ImageGenerationRequest CreateImageRequest(int count = 1);

    /// <summary>The success response payload for a single image submission.</summary>
    protected abstract string SuccessImageSubmitPayload { get; }

    /// <summary>The success response payload for a two-image submission.</summary>
    protected virtual string SuccessImageSubmitPayloadMultiple => throw new NotSupportedException();

    /// <summary>The error response body for an HTTP failure.</summary>
    protected abstract string FailureSubmitBody(int statusCode);

    /// <summary>Whether image submission returns a queued task operation rather than an immediate result.</summary>
    protected virtual bool IsQueuedImageSubmit => false;

    /// <summary>Whether the fixture advertises <see cref="GenerationFeature.MultipleCandidates"/>.</summary>
    protected virtual bool SupportsMultipleCandidates => false;

    /// <summary>The queued-response payload for an image submission.</summary>
    protected virtual string QueuedImageSubmitPayload => throw new NotSupportedException();

    /// <summary>The task id present in <see cref="QueuedImageSubmitPayload"/>.</summary>
    protected virtual string QueuedImageTaskId => throw new NotSupportedException();

    /// <summary>The running status payload.</summary>
    protected virtual string RunningImageStatusPayload => throw new NotSupportedException();

    /// <summary>The succeeded status payload with one asset.</summary>
    protected virtual string SucceededImageStatusPayload => throw new NotSupportedException();

    /// <summary>An unmappable provider status payload.</summary>
    protected virtual string UnknownImageStatusPayload => throw new NotSupportedException();

    /// <summary>The failed status payload.</summary>
    protected virtual string FailedImageStatusPayload => throw new NotSupportedException();

    /// <summary>The canceled status payload.</summary>
    protected virtual string CanceledImageStatusPayload => throw new NotSupportedException();

    [Fact]
    public void Capabilities_ExposeConfiguredFeatures()
    {
        var client = CreateClient(new RecordingHandler());

        client.Capabilities.Features.Should().Be(ImageOnlyCapabilities.Features);
        client.Capabilities.MaximumCandidates.Should().Be(ImageOnlyCapabilities.MaximumCandidates);
        client.Capabilities.InputTransports.Should().BeEquivalentTo(ImageOnlyCapabilities.InputTransports);
    }

    [Fact]
    public async Task SubmitAsync_Image_SucceedsImmediately()
    {
        if (IsQueuedImageSubmit)
            Assert.Skip("Immediate success does not apply to queued providers.");

        var handler = new RecordingHandler().ReturnJson(SuccessImageSubmitPayload);
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result.Should().NotBeNull();
        operation.Result!.Assets.Should().HaveCount(1);
        operation.Result.Assets[0].Source.Should().NotBeNull();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SubmitAsync_Image_ReturnsPinnedHandle()
    {
        var handler = new RecordingHandler().ReturnJson(
            IsQueuedImageSubmit ? QueuedImageSubmitPayload : SuccessImageSubmitPayload);
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        operation.Handle.Provider.Should().Be(ProviderName);
        operation.Handle.EndpointId.Should().Be(EndpointId);
        operation.Handle.Id.Should().NotBeNullOrWhiteSpace();
        operation.Handle.Model.Should().NotBeNullOrWhiteSpace();
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

    [Fact]
    public async Task SubmitAsync_CountBelowOne_ThrowsInvalidRequest()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            CreateImageRequest(count: 0),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.InvalidRequest);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_UnsupportedCandidateCount_Rejected()
    {
        if (SupportsMultipleCandidates)
            Assert.Skip("The fixture advertises multiple candidates.");

        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            CreateImageRequest(count: 2),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_MultipleCandidates_ProducesMultipleAssets()
    {
        if (!SupportsMultipleCandidates)
            Assert.Skip("The fixture does not advertise multiple candidates.");

        var handler = new RecordingHandler().ReturnJson(SuccessImageSubmitPayloadMultiple);
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            CreateImageRequest(count: 2),
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result!.Assets.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(401, GenerationErrorKind.Authentication)]
    [InlineData(403, GenerationErrorKind.Authorization)]
    [InlineData(429, GenerationErrorKind.RateLimited)]
    public async Task SubmitAsync_HttpFailure_ClassifiesCorrectly(
        int statusCode,
        GenerationErrorKind expectedKind)
    {
        var handler = new RecordingHandler().ReturnJson(FailureSubmitBody(statusCode), statusCode);
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<BaizeException>();
        exception.Which.ErrorKind.Should().Be(expectedKind);
        exception.Which.StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public async Task SubmitAsync_MalformedSuccessResponse_ThrowsGenerationFailed()
    {
        var handler = new RecordingHandler().ReturnJson("this is not valid json {");
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task SubmitAsync_ConnectionFailure_ReportsUnknownSubmissionOutcomeAndNeverRetries()
    {
        var handler = new RecordingHandler().ThrowOnSend(new HttpRequestException("connection reset"));
        var client = CreateClient(handler);

        var action = async () => await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnknownSubmissionOutcome);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SubmitAsync_CanceledToken_PropagatesCancellation()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = async () => await client.SubmitAsync(CreateImageRequest(), cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetAsync_WithoutRetrieval_ThrowsUnsupportedCapability()
    {
        if (IsQueuedImageSubmit)
            Assert.Skip("Queued providers advertise operation retrieval.");

        var client = CreateClient(new RecordingHandler());
        var handle = new GenerationOperationHandle(ProviderName, EndpointId, "op-1");

        var action = async () => await client.GetAsync(
            handle,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
    }

    [Fact]
    public async Task CancelAsync_WithoutCancellation_ThrowsUnsupportedCapability()
    {
        if (IsQueuedImageSubmit)
            Assert.Skip("Queued providers advertise cancellation.");

        var client = CreateClient(new RecordingHandler());
        var handle = new GenerationOperationHandle(ProviderName, EndpointId, "op-1");

        var action = async () => await client.CancelAsync(
            handle,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnsupportedCapability);
    }

    [Fact]
    public async Task SubmitAsync_Image_ReturnsQueuedOperationWithHandle()
    {
        if (!IsQueuedImageSubmit)
            Assert.Skip("Immediate providers do not queue image submissions.");

        var handler = new RecordingHandler().ReturnJson(QueuedImageSubmitPayload);
        var client = CreateClient(handler);

        var operation = await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Queued);
        operation.Result.Should().BeNull();
        operation.Handle.Id.Should().Be(QueuedImageTaskId);
    }

    [Fact]
    public async Task GetAsync_QueuedOperation_ReportsRunningThenSucceeded()
    {
        if (!IsQueuedImageSubmit)
            Assert.Skip("Immediate providers do not queue image submissions.");

        var handler = new RecordingHandler()
            .ReturnJson(QueuedImageSubmitPayload)
            .ReturnJson(RunningImageStatusPayload)
            .ReturnJson(SucceededImageStatusPayload);
        var client = CreateClient(handler);

        var submitted = await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        var running = await client.GetAsync(
            submitted.Handle,
            TestContext.Current.CancellationToken);
        running.State.Should().Be(GenerationOperationState.Running);

        var succeeded = await client.GetAsync(
            submitted.Handle,
            TestContext.Current.CancellationToken);
        succeeded.State.Should().Be(GenerationOperationState.Succeeded);
        succeeded.Result.Should().NotBeNull();
        succeeded.Result!.Assets.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAsync_UnknownStatus_RemainsUnknown()
    {
        if (!IsQueuedImageSubmit)
            Assert.Skip("Immediate providers do not queue image submissions.");

        var handler = new RecordingHandler()
            .ReturnJson(QueuedImageSubmitPayload)
            .ReturnJson(UnknownImageStatusPayload);
        var client = CreateClient(handler);

        var submitted = await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        var operation = await client.GetAsync(
            submitted.Handle,
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Unknown);
    }

    [Fact]
    public async Task GetAsync_ProviderFailure_MapsToFailedOperation()
    {
        if (!IsQueuedImageSubmit)
            Assert.Skip("Immediate providers do not queue image submissions.");

        var handler = new RecordingHandler()
            .ReturnJson(QueuedImageSubmitPayload)
            .ReturnJson(FailedImageStatusPayload);
        var client = CreateClient(handler);

        var submitted = await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        var operation = await client.GetAsync(
            submitted.Handle,
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Failed);
        operation.Error.Should().NotBeNull();
        operation.Error!.Kind.Should().Be(GenerationErrorKind.GenerationFailed);
    }

    [Fact]
    public async Task CancelAsync_InvokesProviderCancellation()
    {
        if (!IsQueuedImageSubmit)
            Assert.Skip("Immediate providers do not queue image submissions.");

        var handler = new RecordingHandler()
            .ReturnJson(QueuedImageSubmitPayload)
            .ReturnJson(CanceledImageStatusPayload);
        var client = CreateClient(handler);

        var submitted = await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        var operation = await client.CancelAsync(
            submitted.Handle,
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Canceled);
    }

    [Fact]
    public async Task GetAsync_ConnectionFailure_ReportsProviderUnavailable()
    {
        if (!IsQueuedImageSubmit)
            Assert.Skip("Immediate providers do not queue image submissions.");

        var handler = new RecordingHandler()
            .ReturnJson(QueuedImageSubmitPayload)
            .ThrowOnSend(new HttpRequestException("provider down"));
        var client = CreateClient(handler);

        var submitted = await client.SubmitAsync(
            CreateImageRequest(),
            TestContext.Current.CancellationToken);

        var action = async () => await client.GetAsync(
            submitted.Handle,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.ProviderUnavailable);
    }
}