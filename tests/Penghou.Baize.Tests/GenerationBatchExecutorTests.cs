using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Tests;

public sealed class GenerationBatchExecutorTests
{
    private static readonly GenerationOperationHandle Handle =
        new("Test", "image-endpoint", "op-1", "image-model");

    private static GenerationCapabilities ImageCapabilities(int? maximumCandidates = null) => new()
    {
        Features = GenerationFeature.TextToImage |
                   GenerationFeature.ImageToImage |
                   GenerationFeature.MultipleCandidates |
                   GenerationFeature.OperationRetrieval,
        MaximumCandidates = maximumCandidates
    };

    private static GenerationOperation Succeeded(int assetCount = 1) => new(
        Handle,
        GenerationOperationState.Succeeded,
        new GenerationResult(
            Enumerable.Range(0, assetCount)
                .Select(index => new GeneratedAsset(
                    new InlineGeneratedAssetSource(new byte[] { (byte)index }, "image/png"),
                    ContentType: "image/png"))
                .ToArray()));

    private static IGenerationExecutor CreateExecutor(IGenerationClient client)
    {
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        return new GenerationExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                InitialPollingInterval = TimeSpan.FromMilliseconds(1)
            }));
    }

    private static IGenerationBatchExecutor CreateBatchExecutor(
        IGenerationClient client,
        GenerationCapabilities capabilities,
        out DefaultGenerationClientRegistry registry)
    {
        registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        return new GenerationBatchExecutor(registry);
    }

    private static IGenerationBatchExecutor CreateFastBatchExecutor(
        IGenerationClient client)
    {
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        return new GenerationBatchExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                InitialPollingInterval = TimeSpan.FromMilliseconds(1)
            }));
    }

    private sealed class ScriptedGenerationClient(GenerationCapabilities capabilities)
        : IGenerationClient
    {
        public GenerationCapabilities Capabilities { get; } = capabilities;
        public int SubmitCount { get; private set; }
        public int MaxObservedCount { get; private set; }
        public Func<int, GenerationOperation>? SubmitFactory { get; set; }
        public BaizeException? SubmitError { get; set; }
        public Queue<BaizeException> SubmitErrors { get; set; } = [];
        public List<GenerationOperation> PollScript { get; set; } = [];
        public Queue<BaizeException> GetErrors { get; set; } = [];
        public List<string> CallLog { get; } = [];
        public List<string?> ObservedIdempotencyKeys { get; } = [];
        public int GetCount { get; private set; }
        private readonly object _lock = new();

        public Task<GenerationOperation> SubmitAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                SubmitCount++;
                var count = request is ImageGenerationRequest image ? image.Count : 1;
                MaxObservedCount = Math.Max(MaxObservedCount, count);
                ObservedIdempotencyKeys.Add(request.IdempotencyKey);
                CallLog.Add($"S:{count}");
                if (SubmitError is not null)
                    throw SubmitError;
                if (SubmitErrors.Count > 0)
                    throw SubmitErrors.Dequeue();
                if (PollScript.Count > 0)
                    return Task.FromResult(Queued());
                return Task.FromResult(
                    SubmitFactory?.Invoke(count) ?? Succeeded(count));
            }
        }

        public Task<GenerationOperation> GetAsync(
            GenerationOperationHandle handle,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var index = GetCount;
                GetCount++;
                CallLog.Add("G");
                if (GetErrors.Count > 0)
                    throw GetErrors.Dequeue();
                var result = PollScript.Count == 0
                    ? Succeeded(1)
                    : PollScript[Math.Min(index, PollScript.Count - 1)];
                return Task.FromResult(result);
            }
        }

        public Task<GenerationOperation> CancelAsync(
            GenerationOperationHandle handle,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        private static GenerationOperation Queued() => new(
            Handle,
            GenerationOperationState.Queued);
    }

    [Fact]
    public async Task ExecuteAsync_SplitsByNativeCandidateLimit()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities(maximumCandidates: 4));
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icons" },
                TotalCount: 10),
            cancellationToken: TestContext.Current.CancellationToken);

        result.AllSucceeded.Should().BeTrue();
        result.SucceededCount.Should().Be(3);
        result.FailedCount.Should().Be(0);
        result.Assets.Should().HaveCount(10);
        result.Chunks.Should().HaveCount(3);
        client.SubmitCount.Should().Be(3);
        client.MaxObservedCount.Should().Be(4);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutNativeLimit_ChunksPerRequest()
    {
        var client = new ScriptedGenerationClient(
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToImage | GenerationFeature.OperationRetrieval
            });
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icons" },
                TotalCount: 5),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Assets.Should().HaveCount(5);
        result.Chunks.Should().HaveCount(5);
        client.SubmitCount.Should().Be(5);
        client.MaxObservedCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_IdempotencyKey_IsDerivedDeterministicallyPerChunk()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities(maximumCandidates: 4));
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icons", IdempotencyKey = "batch-9" },
                TotalCount: 10),
            cancellationToken: TestContext.Current.CancellationToken);

        // Chunks submit concurrently; compare as a set, not in completion order.
        client.ObservedIdempotencyKeys.Should().HaveCount(3);
        client.ObservedIdempotencyKeys.Should().BeEquivalentTo(
            ["batch-9-0", "batch-9-1", "batch-9-2"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutKey_ChunkRequestsCarryNoKey()
    {
        var client = new ScriptedGenerationClient(
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToVideo | GenerationFeature.OperationRetrieval
            });
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new VideoGenerationRequest { Prompt = "clip" },
                TotalCount: 3,
                MaxConcurrency: 1),
            cancellationToken: TestContext.Current.CancellationToken);

        client.ObservedIdempotencyKeys.Should().HaveCount(3).And.OnlyContain(key => key == null);
    }

    [Fact]
    public async Task ExecuteAsync_TotalCountOne_SubmitsOnce()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities(maximumCandidates: 4));
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icon" },
                TotalCount: 1),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Assets.Should().ContainSingle();
        client.SubmitCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_NonImageRequest_ChunksAsSingleCandidates()
    {
        var client = new ScriptedGenerationClient(new GenerationCapabilities
        {
            Features = GenerationFeature.TextToVideo | GenerationFeature.OperationRetrieval
        });
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("Runway", "video-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new VideoGenerationRequest { Prompt = "waves" },
                TotalCount: 3),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Assets.Should().HaveCount(3);
        result.Chunks.Should().HaveCount(3);
        client.SubmitCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_PartialFailure_RecordsErrorAndReturnsSuccessfulAssets()
    {
        var client = new ScriptedGenerationClient(
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToImage | GenerationFeature.OperationRetrieval
            })
        {
            SubmitErrors = new Queue<BaizeException>(
            [
                new BaizeException("quota hit", GenerationErrorKind.RateLimited)
            ])
        };
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icons" },
                TotalCount: 3,
                MaxConcurrency: 1),
            cancellationToken: TestContext.Current.CancellationToken);

        result.AllSucceeded.Should().BeFalse();
        result.SucceededCount.Should().Be(2);
        result.FailedCount.Should().Be(1);
        result.Assets.Should().HaveCount(2);
        result.Errors.Should().HaveCount(1)
            .And.OnlyContain(error => error.ErrorKind == GenerationErrorKind.RateLimited);
        result.Chunks.Where(chunk => chunk.Error is not null).Should().ContainSingle()
            .Which.Index.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsOverallProgress()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities(maximumCandidates: 4))
        {
            PollScript =
            [
                new GenerationOperation(Handle, GenerationOperationState.Running, Progress: 0.4),
                Succeeded(2)
            ]
        };
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                InitialPollingInterval = TimeSpan.FromMilliseconds(1)
            }));

        var reports = new List<double>();
        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icons" },
                TotalCount: 2),
            progress: new SynchronousProgress(value => reports.Add(value)),
            cancellationToken: TestContext.Current.CancellationToken);

        result.AllSucceeded.Should().BeTrue();
        reports.Should().NotBeEmpty();
        reports.Should().OnlyContain(value => value >= 0.0 && value <= 1.0);
        reports.Should().Contain(1.0);
    }

    [Fact]
    public async Task ExecuteAsync_QueueAware_SubmitsAllChunksBeforePolling()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities(maximumCandidates: 3))
        {
            PollScript = [Succeeded(3), Succeeded(3)]
        };
        var batchExecutor = CreateFastBatchExecutor(client);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icons" },
                TotalCount: 6,
                MaxConcurrency: 1),
            cancellationToken: TestContext.Current.CancellationToken);

        result.AllSucceeded.Should().BeTrue();
        result.Assets.Should().HaveCount(6);
        result.Chunks.Should().HaveCount(2);
        client.SubmitCount.Should().Be(2);
        client.CallLog.Take(2).Should().OnlyContain(entry => entry == "S:3");
        client.CallLog.Skip(2).Should().OnlyContain(entry => entry == "G");
        client.GetCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_QueueAware_ReturnsAssetsAcrossChunks()
    {
        var client = new ScriptedGenerationClient(
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToImage | GenerationFeature.OperationRetrieval
            })
        {
            PollScript = [Succeeded(1), Succeeded(1), Succeeded(1)]
        };
        var batchExecutor = CreateFastBatchExecutor(client);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icons" },
                TotalCount: 3),
            cancellationToken: TestContext.Current.CancellationToken);

        result.AllSucceeded.Should().BeTrue();
        result.Assets.Should().HaveCount(3);
        result.Chunks.Should().HaveCount(3);
        client.SubmitCount.Should().Be(3);
        client.GetCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_QueueAware_TransientStatusError_RetriedNextWave()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities())
        {
            GetErrors = new Queue<BaizeException>(
            [
                new BaizeException("provider restarting", GenerationErrorKind.ProviderUnavailable)
            ]),
            PollScript = [Succeeded(1)]
        };
        var batchExecutor = CreateFastBatchExecutor(client);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icon" },
                TotalCount: 1),
            cancellationToken: TestContext.Current.CancellationToken);

        result.AllSucceeded.Should().BeTrue();
        result.Assets.Should().ContainSingle();
        client.GetCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_QueueAware_NonRetryableStatusError_RecordsFailure()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities())
        {
            GetErrors = new Queue<BaizeException>(
            [
                new BaizeException("no access", GenerationErrorKind.Authorization)
            ]),
            PollScript = [Succeeded(1)]
        };
        var batchExecutor = CreateFastBatchExecutor(client);

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icon" },
                TotalCount: 1),
            cancellationToken: TestContext.Current.CancellationToken);

        result.AllSucceeded.Should().BeFalse();
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle()
            .Which.ErrorKind.Should().Be(GenerationErrorKind.Authorization);
        client.GetCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_QueueAware_TimesOutQueuedRun_RecordsTimeout()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities())
        {
            PollScript =
            [
                new GenerationOperation(Handle, GenerationOperationState.Running, Progress: 0.4)
            ]
        };
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromMilliseconds(150),
                InitialPollingInterval = TimeSpan.FromMilliseconds(40),
                MaxPollingInterval = TimeSpan.FromMilliseconds(40)
            }));

        var result = await batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icon" },
                TotalCount: 1),
            cancellationToken: TestContext.Current.CancellationToken);

        result.AllSucceeded.Should().BeFalse();
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle()
            .Which.ErrorKind.Should().Be(GenerationErrorKind.TimeoutExceeded);
        result.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NoSuitableEndpoint_Throws()
    {
        var client = new ScriptedGenerationClient(new GenerationCapabilities
        {
            Features = GenerationFeature.TextToVideo
        });
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("Runway", "video-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var action = () => batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icon" },
                TotalCount: 2),
            cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await action.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.InvalidRequest);
        exception.Message.Should().Contain("No configured generation endpoint");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteAsync_InvalidTotalCount_Throws(int totalCount)
    {
        var client = new ScriptedGenerationClient(ImageCapabilities());
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var action = () => batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icon" },
                totalCount),
            cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.InvalidRequest);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidConcurrency_Throws()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities());
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var action = () => batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icon" },
                TotalCount: 2,
                MaxConcurrency: 0),
            cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.InvalidRequest);
    }

    [Fact]
    public async Task ExecuteAsync_CanceledToken_PropagatesCancellation()
    {
        var client = new ScriptedGenerationClient(ImageCapabilities());
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = () => batchExecutor.ExecuteAsync(
            new GenerationBatchRequest(
                new ImageGenerationRequest { Prompt = "icon" },
                TotalCount: 2),
            cancellationToken: cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void AddBaizeGeneration_RegistersBatchExecutor()
    {
        var services = new ServiceCollection();
        services.AddBaizeGeneration(options => options.Timeout = TimeSpan.FromMinutes(1));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IGenerationBatchExecutor>().Should()
            .BeOfType<GenerationBatchExecutor>();
    }
}