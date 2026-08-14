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
/// validates the provider's default (text-to-image) generation case.
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
            $"Provider=OpenAi Model={LiveTestSettings.GenerationModel} " +
            $"ContentType={asset.ContentType} Source={asset.Source.GetType().Name} " +
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
        services.AddBaizeOpenAiGeneration("live-generation", options =>
        {
            options.ApiKey = apiKey;
            options.Model = LiveTestSettings.GenerationModel;
            options.BaseAddress = new Uri(
                settings.BaseUrl ?? "https://api.openai.com/v1");
            options.Features = GenerationFeature.TextToImage;
        });
        return services.BuildServiceProvider(validateScopes: true);
    }
}
