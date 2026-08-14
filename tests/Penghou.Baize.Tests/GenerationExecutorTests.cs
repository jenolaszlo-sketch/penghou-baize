using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Tests;

public sealed class GenerationExecutorTests
{
    private static readonly GenerationOperationHandle Handle =
        new("Test", "image-endpoint", "op-1", "image-model");

    private static GenerationCapabilities ImageCapabilities() => new()
    {
        Features = GenerationFeature.TextToImage |
                   GenerationFeature.ImageToImage |
                   GenerationFeature.MultipleCandidates |
                   GenerationFeature.OperationRetrieval
    };

    private static ImageGenerationRequest ImageRequest(string prompt = "an icon") =>
        new() { Prompt = prompt };

    private static GenerationOperation Succeeded() => new(
        Handle,
        GenerationOperationState.Succeeded,
        new GenerationResult([new GeneratedAsset(
            new InlineGeneratedAssetSource(new byte[] { 1 }, "image/png"),
            ContentType: "image/png")]));

    private static GenerationOperation Queued(double? progress = null) => new(
        Handle,
        GenerationOperationState.Queued,
        Progress: progress);

    private static GenerationOperation Running(double? progress = null) => new(
        Handle,
        GenerationOperationState.Running,
        Progress: progress);

    [Fact]
    public async Task ExecuteAsync_RoutesToFirstSuitableEndpointAndSubmitsOnce()
    {
        var registry = new DefaultGenerationClientRegistry();
        var image = new FakeGenerationClient(ImageCapabilities()) { SubmitResult = Succeeded() };
        var video = new FakeGenerationClient(new GenerationCapabilities
        {
            Features = GenerationFeature.TextToVideo | GenerationFeature.OperationRetrieval
        })
        { SubmitResult = Succeeded() };
        registry.Register("OpenAi", "image-endpoint", image);
        registry.Register("OpenAi", "video-endpoint", video);
        var executor = new GenerationExecutor(registry);

        var result = await executor.ExecuteAsync(
            ImageRequest(), progress: null, TestContext.Current.CancellationToken);

        result.Assets.Should().ContainSingle();
        image.SubmitCount.Should().Be(1);
        video.SubmitCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenNoEndpointSatisfiesRequest()
    {
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "video-endpoint", new FakeGenerationClient(
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToVideo | GenerationFeature.OperationRetrieval
            }));
        var executor = new GenerationExecutor(registry);

        var action = () => executor.ExecuteAsync(
            ImageRequest(), progress: null, TestContext.Current.CancellationToken);

        var exception = (await action.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.InvalidRequest);
        exception.Message.Should().Contain("No configured generation endpoint");
    }

    [Fact]
    public async Task ExecuteAsync_PollsUntilSucceededAndReportsProgress()
    {
        var registry = new DefaultGenerationClientRegistry();
        var client = new FakeGenerationClient(ImageCapabilities())
        {
            SubmitResult = Queued(),
            PollScript =
            [
                Running(0.4),
                Running(0.7),
                Succeeded()
            ]
        };
        registry.Register("OpenAi", "image-endpoint", client);
        var executor = new GenerationExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                InitialPollingInterval = TimeSpan.FromMilliseconds(1)
            }));

        var reports = new List<double>();
        var result = await executor.ExecuteAsync(
            ImageRequest(),
            progress: new Progress<double>(value => reports.Add(value)),
            TestContext.Current.CancellationToken);

        result.Assets.Should().ContainSingle();
        client.SubmitCount.Should().Be(1);
        client.GetCount.Should().Be(3);
        reports.Should().BeEquivalentTo([0.4, 0.7]);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesTransientStatusReads()
    {
        var registry = new DefaultGenerationClientRegistry();
        var client = new FakeGenerationClient(ImageCapabilities())
        {
            SubmitResult = Queued(),
            PollScript = [Succeeded()],
            GetErrors =
            [
                BaizeException.ProviderUnavailable("boom")
            ]
        };
        registry.Register("OpenAi", "image-endpoint", client);
        var executor = new GenerationExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                InitialPollingInterval = TimeSpan.FromMilliseconds(1)
            }));

        var result = await executor.ExecuteAsync(
            ImageRequest(), progress: null, TestContext.Current.CancellationToken);

        result.Assets.Should().ContainSingle();
        client.GetCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsTimeoutExceededWithResumableHandle()
    {
        var registry = new DefaultGenerationClientRegistry();
        var client = new FakeGenerationClient(ImageCapabilities())
        {
            SubmitResult = Queued(),
            PollScript = [Running(0.5)]
        };
        registry.Register("OpenAi", "image-endpoint", client);
        var executor = new GenerationExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromMilliseconds(120),
                InitialPollingInterval = TimeSpan.FromMilliseconds(10),
                MaxPollingInterval = TimeSpan.FromMilliseconds(10)
            }));

        var action = () => executor.ExecuteAsync(
            ImageRequest(), progress: null, TestContext.Current.CancellationToken);

        var exception = (await action.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.TimeoutExceeded);
        exception.Message.Should().Contain(Handle.Id);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesFailedAndCanceledOperations()
    {
        var registry = new DefaultGenerationClientRegistry();
        var failed = new FakeGenerationClient(ImageCapabilities())
        {
            SubmitResult = new GenerationOperation(
                Handle,
                GenerationOperationState.Failed,
                Error: new GenerationError(
                    GenerationErrorKind.SafetyRejected,
                    "rejected by policy"))
        };
        registry.Register("OpenAi", "failed-endpoint", failed);
        var executor = new GenerationExecutor(registry);

        var failedAction = () => executor.ExecuteAsync(
            ImageRequest(), progress: null, TestContext.Current.CancellationToken);
        var failedException = (await failedAction.Should().ThrowAsync<BaizeException>()).Which;
        failedException.ErrorKind.Should().Be(GenerationErrorKind.SafetyRejected);
        failedException.Message.Should().Be("rejected by policy");

        var canceled = new FakeGenerationClient(ImageCapabilities())
        {
            SubmitResult = new GenerationOperation(Handle, GenerationOperationState.Canceled)
        };
        registry.Register("OpenAi", "canceled-endpoint", canceled);
        var canceledAction = () => executor.ExecuteAsync(
            ImageRequest(), progress: null, TestContext.Current.CancellationToken);
        var canceledException = (await canceledAction.Should().ThrowAsync<BaizeException>()).Which;
        canceledException.ErrorKind.Should().Be(GenerationErrorKind.Canceled);
    }

    [Fact]
    public async Task ExecuteAsync_SurfacesUnknownSubmissionOutcomeWithoutRetrying()
    {
        var registry = new DefaultGenerationClientRegistry();
        var client = new FakeGenerationClient(ImageCapabilities())
        {
            SubmitError = BaizeException.UnknownSubmissionOutcome("connection dropped")
        };
        registry.Register("OpenAi", "image-endpoint", client);
        var executor = new GenerationExecutor(registry);

        var action = () => executor.ExecuteAsync(
            ImageRequest(), progress: null, TestContext.Current.CancellationToken);

        var exception = (await action.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.UnknownSubmissionOutcome);
        client.SubmitCount.Should().Be(1);
    }

    [Fact]
    public void DefaultRoutingPolicy_SelectsFirstCandidate()
    {
        var policy = new DefaultGenerationRoutingPolicy();
        var first = new GenerationEndpoint(
            "OpenAi", "a", new FakeGenerationClient(ImageCapabilities()));
        var second = new GenerationEndpoint(
            "OpenAi", "b", new FakeGenerationClient(ImageCapabilities()));

        policy.Select(ImageRequest(), [first, second]).Should().BeSameAs(first);
        policy.Select(ImageRequest(), []).Should().BeNull();
    }

    [Fact]
    public void AddBaizeGeneration_RegistersExecutorRoutingAndRegistry()
    {
        var services = new ServiceCollection();
        services.AddBaizeGeneration(options => options.Timeout = TimeSpan.FromMinutes(1));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IGenerationClientRegistry>().Should()
            .BeOfType<DefaultGenerationClientRegistry>();
        provider.GetRequiredService<IGenerationRoutingPolicy>().Should()
            .BeOfType<DefaultGenerationRoutingPolicy>();
        provider.GetRequiredService<IGenerationExecutor>().Should()
            .BeOfType<GenerationExecutor>();
    }

    private sealed class FakeGenerationClient(GenerationCapabilities capabilities)
        : IGenerationClient
    {
        public GenerationCapabilities Capabilities { get; } = capabilities;
        public GenerationOperation SubmitResult { get; set; } = new(
            new GenerationOperationHandle("Test", "image-endpoint", "op-1"),
            GenerationOperationState.Queued);
        public BaizeException? SubmitError { get; set; }
        public List<GenerationOperation> PollScript { get; set; } = [];
        public List<BaizeException> GetErrors { get; set; } = [];
        public int SubmitCount { get; private set; }
        public int GetCount { get; private set; }
        private readonly object _lock = new();

        public Task<GenerationOperation> SubmitAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            SubmitCount++;
            if (SubmitError is not null)
                throw SubmitError;
            return Task.FromResult(SubmitResult);
        }

        public Task<GenerationOperation> GetAsync(
            GenerationOperationHandle handle,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (GetCount < GetErrors.Count)
                {
                    var error = GetErrors[GetCount];
                    GetCount++;
                    throw error;
                }

                var result = PollScript.Count == 0
                    ? SubmitResult
                    : PollScript[Math.Min(GetCount, PollScript.Count - 1)];
                GetCount++;
                return Task.FromResult(result);
            }
        }

        public Task<GenerationOperation> CancelAsync(
            GenerationOperationHandle handle,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
