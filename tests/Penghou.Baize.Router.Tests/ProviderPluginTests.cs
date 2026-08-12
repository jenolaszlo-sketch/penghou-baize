using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;
using Penghou.Baize.TestProvider;
using Penghou.Baize.Claude;
using Penghou.Baize.Gemini;
using Penghou.Baize.Ollama;
using Penghou.Baize.OpenAi;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Penghou.Baize.Router.Tests;

public sealed class ProviderPluginTests
{
    [Fact]
    public void BuiltInProviderPackages_CanBeRegisteredExplicitly()
    {
        var services = new ServiceCollection();

        services.AddOpenAiLlmProvider();
        services.AddClaudeLlmProvider();
        services.AddGeminiLlmProvider();
        services.AddOllamaLlmProvider();

        using var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetServices<ILlmClientProvider>()
            .Select(provider => provider.Key.ToString())
            .Should().BeEquivalentTo("OpenAi", "Claude", "Gemini", "Ollama");
    }

    [Fact]
    public async Task AddLlmRouting_LoadsConfiguredProviderAssemblyByName()
    {
        var configuration = Configuration(
            assembly: "Penghou.Baize.TestProvider",
            type: typeof(TestLlmClientProvider).FullName!);
        var services = new ServiceCollection();
        services.AddHttpClient();

        services.AddLlmRouting(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider
            .GetRequiredService<ILlmModelLookup>()
            .GetClient("custom-model", new LlmProviderKey("CUSTOM-TEST"));

        client.Capabilities.NativeToolCalling.Should().BeTrue();
        var metadata = client.Should()
            .BeAssignableTo<ILlmClientMetadataProvider>().Subject.Metadata;
        metadata.Provider.Should().Be("custom-test");
        metadata.Model.Should().Be("provider-model");
        metadata.Endpoint.Should().Be(new Uri("https://custom-provider.example/v1"));

        var events = new List<LlmStreamEvent>();
        await foreach (var item in client.StreamAsync(
                           new LlmRequest([new LlmMessage("user", "hello")]),
                           TestContext.Current.CancellationToken))
            events.Add(item);

        events.Should().Contain(item => item.Delta == "custom-provider");
    }

    [Fact]
    public async Task AddLlmRouting_DiscoversAllPublicProvidersWhenTypeIsOmitted()
    {
        var configuration = Configuration(
            assembly: "Penghou.Baize.TestProvider",
            type: null);
        var services = new ServiceCollection();
        services.AddHttpClient();

        services.AddLlmRouting(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<ILlmModelLookup>()
            .GetClient("custom-model", "custom-test");
        var events = new List<LlmStreamEvent>();
        await foreach (var item in client.StreamAsync(
                           new LlmRequest([new LlmMessage("user", "hello")]),
                           TestContext.Current.CancellationToken))
            events.Add(item);

        events.Should().Contain(item => item.Delta == "custom-provider");
    }

    [Theory]
    [InlineData(@".\plugins\Penghou.Baize.TestProvider.dll")]
    [InlineData("./plugins/Penghou.Baize.TestProvider.dll")]
    public void AddLlmRouting_RejectsProviderAssemblyPaths(string assemblyPath)
    {
        var configuration = Configuration(
            assembly: assemblyPath,
            type: null);
        var services = new ServiceCollection();

        var action = () => services.AddLlmRouting(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*assembly name, not a path*");
    }

    [Fact]
    public void ProviderRegistry_RejectsDuplicateKeysIgnoringCase()
    {
        var providers = new ILlmClientProvider[]
        {
            new TestLlmClientProvider(),
            new DuplicateTestLlmClientProvider()
        };

        var action = () => new LlmClientProviderRegistry(providers);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*more than one LLM client provider*CUSTOM-TEST*unique*");
    }

    [Fact]
    public void ProviderRegistry_ExplainsHowToRegisterMissingProvider()
    {
        var registry = new LlmClientProviderRegistry([new TestLlmClientProvider()]);

        var action = () => registry.GetRequiredProvider("missing");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing*custom-test*ProviderModules*");
    }

    [Fact]
    public void ProviderModuleLoading_EmitsContentFreeTelemetry()
    {
        var activities = new ConcurrentQueue<Activity>();
        var loadMetricObserved = 0;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == BaizeTelemetry.InstrumentationName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(activityListener);
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BaizeTelemetry.InstrumentationName &&
                    instrument.Name == "baize.provider.module.loads")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (tags.ToArray().Any(tag =>
                    tag.Key == "baize.provider.module.assembly" &&
                    Equals(tag.Value, "Penghou.Baize.TestProvider")))
            {
                Interlocked.Exchange(ref loadMetricObserved, 1);
            }
        });
        meterListener.Start();
        var services = new ServiceCollection();

        services.AddLlmRouting(Configuration(
            "Penghou.Baize.TestProvider",
            typeof(TestLlmClientProvider).FullName));

        activities.Should().Contain(activity =>
            activity.OperationName == "llm.provider.module.load" &&
            Equals(
                activity.GetTagItem("baize.provider.module.assembly"),
                "Penghou.Baize.TestProvider"));
        Volatile.Read(ref loadMetricObserved).Should().Be(1);
    }

    private static IConfiguration Configuration(string assembly, string? type)
    {
        var values = new Dictionary<string, string?>
        {
            ["LlmRouting:ProviderModules:0:Assembly"] = assembly,
            ["LlmRouting:Models:0:Name"] = "custom-model",
            ["LlmRouting:Models:0:Endpoints:0:Provider"] = "custom-test",
            ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] = "provider-model",
            ["LlmRouting:Models:0:Endpoints:0:Settings:Mode"] = "strict"
        };

        if (type is not null)
            values["LlmRouting:ProviderModules:0:Type"] = type;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class DuplicateTestLlmClientProvider : ILlmClientProvider
    {
        public LlmProviderKey Key { get; } = new("CUSTOM-TEST");

        public string DefaultBaseUrl => "https://duplicate.example";

        public LlmEndpointCapabilities DefaultCapabilities { get; } = new();

        public ILlmClient CreateClient(LlmClientProviderContext context) =>
            throw new NotSupportedException();
    }
}
