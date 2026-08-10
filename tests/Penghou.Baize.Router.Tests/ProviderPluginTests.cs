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
    public void AddLlmRouting_LoadsConfiguredProviderAssemblyByName()
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

        var customClient = client.Should().BeOfType<TestLlmClient>().Subject;
        customClient.Model.Should().Be("provider-model");
        customClient.BaseUrl.Should().Be("https://custom-provider.example/v1");
        customClient.Settings.Should().Contain("Mode", "strict");
        customClient.Capabilities.NativeToolCalling.Should().BeTrue();
    }

    [Fact]
    public void AddLlmRouting_DiscoversAllPublicProvidersWhenTypeIsOmitted()
    {
        var configuration = Configuration(
            assembly: "Penghou.Baize.TestProvider",
            type: null);
        var services = new ServiceCollection();
        services.AddHttpClient();

        services.AddLlmRouting(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetRequiredService<ILlmModelLookup>()
            .GetClient("custom-model", "custom-test")
            .Should().BeOfType<TestLlmClient>();
    }

    [Fact]
    public void AddLlmRouting_RejectsProviderAssemblyPaths()
    {
        var configuration = Configuration(
            assembly: ".\\plugins\\Penghou.Baize.TestProvider.dll",
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
