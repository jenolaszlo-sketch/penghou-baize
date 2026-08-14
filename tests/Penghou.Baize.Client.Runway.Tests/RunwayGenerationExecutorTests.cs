using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;

namespace Penghou.Baize.Runway.Tests;

/// <summary>
/// Validates the genuinely queued, long-running Runway path through the
/// <see cref="IGenerationExecutor"/> contract: submit returns queued, the
/// executor polls status transitions, and a terminal task yields assets.
/// </summary>
public sealed class RunwayGenerationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_PollsRunwayTaskUntilSucceededAndReportsProgress()
    {
        var handler = new RecordingHandler()
            .ReturnJson("""{"id":"task-1","estimatedCost":{"credits":40}}""")
            .ReturnJson("""{"id":"task-1","status":"THROTTLED"}""")
            .ReturnJson("""{"id":"task-1","status":"RUNNING","progress":0.4}""")
            .ReturnJson("""{"id":"task-1","status":"RUNNING","progress":0.8}""")
            .ReturnJson(
                """{"id":"task-1","status":"SUCCEEDED","output":["https://cdn.test/v1.mp4"]}""");
        var client = new RunwayGenerationClient(
            model: "gen4.5",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            new Uri("https://api.dev.runwayml.com/v1"),
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToVideo |
                           GenerationFeature.ImageToVideo |
                           GenerationFeature.OperationRetrieval |
                           GenerationFeature.Cancellation |
                           GenerationFeature.Progress,
                InputTransports = new HashSet<LlmContentTransport>
                {
                    LlmContentTransport.Uri,
                    LlmContentTransport.InlineData
                }
            },
            "runway-gen-1");

        var registry = new DefaultGenerationClientRegistry();
        registry.Register("Runway", "runway-gen-1", client);
        var executor = new GenerationExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                InitialPollingInterval = TimeSpan.FromMilliseconds(1)
            }));

        var reports = new List<double>();
        var result = await executor.ExecuteAsync(
            new VideoGenerationRequest { Prompt = "a drone shot over a coastline" },
            progress: new Progress<double>(value => reports.Add(value)),
            TestContext.Current.CancellationToken);

        result.Assets.Should().ContainSingle()
            .Which.Source.As<UriGeneratedAssetSource>().Uri.ToString().Should().Be("https://cdn.test/v1.mp4");
        reports.Should().BeEquivalentTo([0.4, 0.8]);

        handler.Requests.Count.Should().Be(5);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/v1/text_to_video");
        handler.Requests.Skip(1).Select(request => request.RequestUri!.AbsolutePath)
            .Should().OnlyContain(path => path == "/v1/tasks/task-1");
    }

    [Fact]
    public async Task ExecuteAsync_SubmitTimeout_SurfacesUnknownOutcomeAndNoRetry()
    {
        var handler = new RecordingHandler().ThrowOnSend(new HttpRequestException("connection reset"));
        var client = new RunwayGenerationClient(
            model: "gen4.5",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            new Uri("https://api.dev.runwayml.com/v1"),
            new GenerationCapabilities
            {
                Features = GenerationFeature.TextToVideo |
                           GenerationFeature.ImageToVideo |
                           GenerationFeature.OperationRetrieval |
                           GenerationFeature.Cancellation |
                           GenerationFeature.Progress,
                InputTransports = new HashSet<LlmContentTransport>
                {
                    LlmContentTransport.Uri,
                    LlmContentTransport.InlineData
                }
            },
            "runway-gen-1");

        var registry = new DefaultGenerationClientRegistry();
        registry.Register("Runway", "runway-gen-1", client);
        var executor = new GenerationExecutor(registry);

        var action = () => executor.ExecuteAsync(
            new VideoGenerationRequest { Prompt = "clip" },
            progress: null,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<BaizeException>()
            .Where(exception => exception.ErrorKind == GenerationErrorKind.UnknownSubmissionOutcome);
        handler.Requests.Count.Should().Be(1);
    }

    [Fact]
    public void AddBaizeGeneration_ThenRunwayRegistration_RegistersExecutor()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddBaizeGeneration(options => options.Timeout = TimeSpan.FromMinutes(1));
        services.AddBaizeRunwayGeneration("runway-gen-1", options => options.ApiKey = "secret");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IGenerationExecutor>().Should().BeOfType<GenerationExecutor>();
        provider.GetRequiredKeyedService<IGenerationClient>("runway-gen-1").Should()
            .BeOfType<RunwayGenerationClient>();
    }
}