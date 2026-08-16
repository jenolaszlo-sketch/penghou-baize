using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Penghou.Baize.Diagnostics;
using Penghou.Baize.Generation;
using Penghou.Baize.OpenAi;

namespace Penghou.Baize.IntegrationTests;

/// <summary>
/// Opt-in live probe for the OpenAI artifact-generation client. Uses the real
/// <see cref="IGenerationClient"/> through DI, exactly like an application, and
/// validates each generation modality against the provider: text-to-image,
/// image editing, video, and speech. Every test is skipped unless
/// <c>BAIZE_RUN_LIVE_TESTS=1</c> and <c>BAIZE_LIVE_TEST_GENERATION=1</c> are
/// set and the provider is OpenAI.
/// </summary>
public sealed class OpenAiGenerationLiveTests(ITestOutputHelper output)
{
    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.ImageGeneration)]
    public async Task ImageGeneration_ThroughGenerationClient_ReturnsBinaryImage()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.GenerationEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_GENERATION=1 for the OpenAI generation-client probe.");
        if (!settings.Provider.Equals("OpenAi", StringComparison.OrdinalIgnoreCase))
            Assert.Skip("The generation-client probe currently targets OpenAI only.");

        var apiKey = Environment.GetEnvironmentVariable(settings.SecretName!);
        apiKey.Should().NotBeNullOrWhiteSpace();

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = CreateProvider(settings, apiKey);
        var client = provider.GetRequiredKeyedService<IGenerationClient>("live-generation");

        var operation = await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt =
                    "Create a simple flat icon of one blue circle centered on a " +
                    "white background. No text."
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result.Should().NotBeNull();
        operation.Result!.Assets.Should().NotBeEmpty();
        var asset = operation.Result.Assets[0];
        asset.ContentType.Should().StartWith("image/");
        output.WriteLine(
            $"Provider=OpenAi Modality=text-to-image " +
            $"Model={LiveTestSettings.GenerationModel} " +
            $"ContentType={asset.ContentType} Source={asset.Source.GetType().Name} " +
            $"Captured diagnostics: {settings.DiagnosticsDirectory}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.ImageGeneration)]
    public async Task ImageEdit_ThroughGenerationClient_ReturnsBinaryImage()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.GenerationEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_GENERATION=1 for the OpenAI generation-client probe.");
        if (!settings.Provider.Equals("OpenAi", StringComparison.OrdinalIgnoreCase))
            Assert.Skip("The generation-client probe currently targets OpenAI only.");

        var apiKey = Environment.GetEnvironmentVariable(settings.SecretName!);
        apiKey.Should().NotBeNullOrWhiteSpace();

        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "solid-red.png.base64");
        var imageBytes = Convert.FromBase64String(
            (await File.ReadAllTextAsync(
                fixturePath,
                TestContext.Current.CancellationToken)).Trim());

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = CreateProvider(settings, apiKey);
        var client = provider.GetRequiredKeyedService<IGenerationClient>("live-generation");

        var operation = await client.SubmitAsync(
            new ImageGenerationRequest
            {
                Prompt = "Turn the red shape into a blue shape. Keep everything else unchanged.",
                Inputs = [new LlmInlineDataSource(imageBytes)]
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result.Should().NotBeNull();
        operation.Result!.Assets.Should().NotBeEmpty();
        operation.Result.Assets[0].ContentType.Should().StartWith("image/");
        output.WriteLine(
            $"Provider=OpenAi Modality=image-edit " +
            $"Model={LiveTestSettings.GenerationModel} " +
            $"InputBytes={imageBytes.Length} " +
            $"OutputSource={operation.Result.Assets[0].Source.GetType().Name}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.VideoInput)]
    public async Task VideoGeneration_ThroughGenerationClient_ReturnsQueuedOperationThenAssets()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.GenerationEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_GENERATION=1 for the OpenAI generation-client probe.");
        if (!settings.Provider.Equals("OpenAi", StringComparison.OrdinalIgnoreCase))
            Assert.Skip("The generation-client probe currently targets OpenAI only.");

        var apiKey = Environment.GetEnvironmentVariable(settings.SecretName!);
        apiKey.Should().NotBeNullOrWhiteSpace();

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = CreateProvider(settings, apiKey);
        var client = provider.GetRequiredKeyedService<IGenerationClient>("live-generation");

        var operation = await client.SubmitAsync(
            new VideoGenerationRequest
            {
                Prompt =
                    "A slow pan across a calm blue ocean at sunset, gentle waves, " +
                    "no text, no people. Five seconds."
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Queued);
        operation.Handle.Id.Should().NotBeNullOrWhiteSpace();
        output.WriteLine(
            $"Provider=OpenAi Modality=video Model={LiveTestSettings.GenerationVideoModel} " +
            $"Handle={operation.Handle.Id}");

        // Poll the queued video operation to a terminal state, bounded by the
        // configured live HTTP timeout. A conservative probe may cancel instead.
        using var cts = new CancellationTokenSource(settings.HttpTimeout);
        GenerationOperation current = operation;
        while (current.State is not GenerationOperationState.Succeeded
            and not GenerationOperationState.Failed
            and not GenerationOperationState.Canceled)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
            current = await client.GetAsync(current.Handle, cts.Token);
            output.WriteLine(
                $"Provider=OpenAi Video status: {current.State} " +
                $"progress={current.Progress?.ToString("P0")}");
        }

        current.State.Should().Be(GenerationOperationState.Succeeded);
        current.Result.Should().NotBeNull();
        current.Result!.Assets.Should().NotBeEmpty();
        output.WriteLine(
            $"Provider=OpenAi Modality=video completed " +
            $"Assets={current.Result.Assets.Count} " +
            $"ContentType={current.Result.Assets[0].ContentType}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.AudioInput)]
    public async Task SpeechGeneration_ThroughGenerationClient_ReturnsBinaryAudio()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.GenerationEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_GENERATION=1 for the OpenAI generation-client probe.");
        if (!settings.Provider.Equals("OpenAi", StringComparison.OrdinalIgnoreCase))
            Assert.Skip("The generation-client probe currently targets OpenAI only.");

        var apiKey = Environment.GetEnvironmentVariable(settings.SecretName!);
        apiKey.Should().NotBeNullOrWhiteSpace();

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = CreateProvider(settings, apiKey);
        var client = provider.GetRequiredKeyedService<IGenerationClient>("live-generation");

        var operation = await client.SubmitAsync(
            new AudioGenerationRequest
            {
                Prompt = "Hello from Baize. This is a short spoken test message.",
                OutputFormat = "mp3"
            },
            TestContext.Current.CancellationToken);

        operation.State.Should().Be(GenerationOperationState.Succeeded);
        operation.Result.Should().NotBeNull();
        operation.Result!.Assets.Should().NotBeEmpty();
        var asset = operation.Result.Assets[0];
        asset.ContentType.Should().StartWith("audio/");
        asset.Source.As<InlineGeneratedAssetSource>().Data.Length.Should().BeGreaterThan(0);
        output.WriteLine(
            $"Provider=OpenAi Modality=speech " +
            $"Model={LiveTestSettings.GenerationAudioModel} " +
            $"ContentType={asset.ContentType} Bytes={asset.Size}");
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
        services.AddBaizeOpenAiGeneration("live-generation", options =>
        {
            options.ApiKey = apiKey;
            options.Model = LiveTestSettings.GenerationModel;
            options.ImageModel = LiveTestSettings.GenerationModel;
            options.VideoModel = LiveTestSettings.GenerationVideoModel;
            options.AudioModel = LiveTestSettings.GenerationAudioModel;
            options.BaseAddress = new Uri(
                settings.BaseUrl ?? "https://api.openai.com/v1");
            options.Features =
                GenerationFeature.TextToImage |
                GenerationFeature.ImageToImage |
                GenerationFeature.TextToVideo |
                GenerationFeature.TextToSpeech |
                GenerationFeature.OperationRetrieval |
                GenerationFeature.Cancellation |
                GenerationFeature.Progress;
        });
        return services.BuildServiceProvider(validateScopes: true);
    }
}