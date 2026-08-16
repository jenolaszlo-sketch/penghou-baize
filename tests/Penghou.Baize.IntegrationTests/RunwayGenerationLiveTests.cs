using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Penghou.Baize.Diagnostics;
using Penghou.Baize.Generation;
using Penghou.Baize.Runway;

namespace Penghou.Baize.IntegrationTests;

/// <summary>
/// Opt-in live probe for the Runway video-generation client. Uses the real
/// <see cref="IGenerationClient"/> through DI, exactly like an application, and
/// validates the genuinely queued path: submission returns a task id, the
/// operation is polled to a terminal state, and the output video asset is
/// surfaced. Skipped unless <c>BAIZE_RUN_LIVE_TESTS=1</c>,
/// <c>BAIZE_LIVE_TEST_GENERATION=1</c>, and <c>BAIZE_LIVE_PROVIDER=Runway</c>
/// are set.
/// </summary>
public sealed class RunwayGenerationLiveTests(ITestOutputHelper output)
{
    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.ImageGeneration)]
    public async Task TextToVideo_ThroughGenerationClient_PollsQueuedTaskToAssets()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.GenerationEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_GENERATION=1 for the Runway generation-client probe.");
        if (!settings.Provider.Equals("Runway", StringComparison.OrdinalIgnoreCase))
            Assert.Skip("The Runway generation-client probe requires BAIZE_LIVE_PROVIDER=Runway.");

        var apiKey = Environment.GetEnvironmentVariable(settings.SecretName!);
        apiKey.Should().NotBeNullOrWhiteSpace();

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = CreateProvider(settings, apiKey);
        var client = provider.GetRequiredKeyedService<IGenerationClient>("live-generation");
        var executor = provider.GetRequiredService<IGenerationExecutor>();

        var result = await executor.ExecuteAsync(
            new VideoGenerationRequest
            {
                Prompt =
                    "A slow aerial shot moving across a coastline at golden hour, " +
                    "calm water, gentle waves, no text, no people.",
                Duration = TimeSpan.FromSeconds(5)
            },
            progress: new Progress<double>(value =>
                output.WriteLine($"Provider=Runway progress={value:P0}")),
            TestContext.Current.CancellationToken);

        result.Assets.Should().NotBeEmpty();
        var asset = result.Assets[0];
        asset.ContentType.Should().StartWith("video/");
        output.WriteLine(
            $"Provider=Runway Model={LiveTestSettings.GenerationModel} " +
            $"ContentType={asset.ContentType} " +
            $"Source={asset.Source.GetType().Name} " +
            $"Captured diagnostics: {settings.DiagnosticsDirectory}");
    }

    private static ServiceProvider CreateProvider(
        LiveTestSettings settings,
        string apiKey)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("llm", client =>
            client.Timeout = settings.HttpTimeout);
        services.AddLogging(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));
        services.AddBaizeHttpDiagnostics(options =>
        {
            options.Enabled = true;
            options.DirectoryPath = settings.DiagnosticsDirectory;
            options.MaxBodyBytes = 1024 * 1024;
            options.MaxRetainedSessions = 100;
        });
        services.AddBaizeGeneration(options =>
            options.Timeout = settings.HttpTimeout);
        services.AddBaizeRunwayGeneration("live-generation", options =>
        {
            options.ApiKey = apiKey;
            options.Model = LiveTestSettings.GenerationModel;
            options.DefaultRatio = "1280:720";
        });
        return services.BuildServiceProvider(validateScopes: true);
    }
}