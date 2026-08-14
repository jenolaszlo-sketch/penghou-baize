using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;

namespace Penghou.Baize.Fal.Tests;

/// <summary>
/// Validates the fal queue path through the <see cref="IGenerationExecutor"/>
/// contract: submit returns queued, the executor polls status transitions, and
/// a completed request yields storage-backed assets. Unlike Runway, fal reports
/// no numeric progress (it exposes a queue position instead), so the executor
/// never raises progress callbacks for a fal endpoint.
/// </summary>
public sealed class FalGenerationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_PollsFalQueueUntilSucceededWithoutNumericProgress()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"request_id":"r-1","status":"IN_QUEUE"}""")
            .ReturnJson("""{"request_id":"r-1","status":"IN_QUEUE","position":2}""")
            .ReturnJson(
                """{"request_id":"r-1","status":"IN_PROGRESS","metrics":{"total_time":3.5}}""")
            .ReturnJson("""{"request_id":"r-1","status":"COMPLETED"}""")
            .ReturnJson(
                """{"images":[{"url":"https://v3.fal.media/files/ok.png"}],"video":{"url":"https://v3.fal.media/files/clip.mp4"}}""");
        var client = new FalGenerationClient(
            model: "fal-ai/flux/dev",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            baseUrl: "https://queue.fal.run",
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToImage |
                           GenerationFeature.OperationRetrieval |
                           GenerationFeature.Cancellation,
                InputTransports = new HashSet<LlmContentTransport>
                {
                    LlmContentTransport.Uri,
                    LlmContentTransport.InlineData
                }
            },
            "fal-gen-1");

        var registry = new DefaultGenerationClientRegistry();
        registry.Register("Fal", "fal-gen-1", client);
        var executor = new GenerationExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                InitialPollingInterval = TimeSpan.FromMilliseconds(1)
            }));

        var reports = new List<double>();
        var result = await executor.ExecuteAsync(
            new ImageGenerationRequest { Prompt = "an alpine lake" },
            progress: new Progress<double>(value => reports.Add(value)),
            TestContext.Current.CancellationToken);

        result.Assets.Should().HaveCount(2);
        result.Assets[0].Source.As<UriGeneratedAssetSource>().Uri.ToString()
            .Should().Be("https://v3.fal.media/files/ok.png");
        reports.Should().BeEmpty();

        handler.Requests.Count.Should().Be(5);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/fal-ai/flux/dev");
        handler.Requests.Skip(1).Take(3).Select(request => request.RequestUri!.AbsolutePath)
            .Should().OnlyContain(path => path == "/requests/r-1/status");
        handler.Requests[4].RequestUri!.AbsolutePath.Should().Be("/requests/r-1");
    }

    [Fact]
    public async Task ExecuteAsync_SubmitTimeout_SurfacesUnknownOutcomeAndNoRetry()
    {
        var handler = new RecordingHandler().ThrowOnSend(new HttpRequestException("connection reset"));
        var client = new FalGenerationClient(
            model: "fal-ai/flux/dev",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            baseUrl: "https://queue.fal.run",
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToImage |
                           GenerationFeature.OperationRetrieval |
                           GenerationFeature.Cancellation,
                InputTransports = new HashSet<LlmContentTransport>
                {
                    LlmContentTransport.Uri,
                    LlmContentTransport.InlineData
                }
            },
            "fal-gen-1");

        var registry = new DefaultGenerationClientRegistry();
        registry.Register("Fal", "fal-gen-1", client);
        var executor = new GenerationExecutor(registry);

        var action = () => executor.ExecuteAsync(
            new ImageGenerationRequest { Prompt = "an icon" },
            progress: null,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnknownSubmissionOutcome);
        handler.Requests.Count.Should().Be(1);
    }

    [Fact]
    public void AddBaizeGeneration_ThenFalRegistration_RegistersExecutor()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddBaizeGeneration(options => options.Timeout = TimeSpan.FromMinutes(1));
        services.AddBaizeFalGeneration("fal-gen-1", options => options.ApiKey = "secret");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IGenerationExecutor>().Should().BeOfType<GenerationExecutor>();
        provider.GetRequiredKeyedService<IGenerationClient>("fal-gen-1").Should()
            .BeOfType<FalGenerationClient>();
    }
}