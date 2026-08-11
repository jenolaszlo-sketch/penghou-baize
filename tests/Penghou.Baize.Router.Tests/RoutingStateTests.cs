using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Ollama;
using Penghou.Baize.Router.Extensions;

namespace Penghou.Baize.Router.Tests;

public sealed class RoutingStateTests
{
    [Fact]
    public void AddLlmRouting_SharesOneAtomicRuntimeAcrossPublicServices()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddOllamaLlmProvider();
        services.AddLlmRouting(Configuration());

        using var provider = services.BuildServiceProvider();
        var lookup = provider.GetRequiredService<ILlmModelLookup>();
        var router = provider.GetRequiredService<ILlmRouter>();
        var validator = provider.GetRequiredService<ILlmEndpointValidator>();

        router.Should().BeSameAs(lookup);
        validator.Should().BeSameAs(lookup);
    }

    [Fact]
    public async Task EndpointValidator_ReportsMissingSecretWithoutSendingRequest()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<ISecretProvider>(new StubSecretProvider(null));
        services.AddOllamaLlmProvider();
        services.AddLlmRouting(Configuration(secretName: "MISSING_KEY"));

        await using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<ILlmEndpointValidator>()
            .ValidateAsync(TestContext.Current.CancellationToken);

        report.Succeeded.Should().BeFalse();
        report.Endpoints.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new
            {
                EndpointId = "ollama",
                Provider = "Ollama",
                Model = "qwen",
                Succeeded = false
            });
        report.Endpoints[0].Error.Should().Contain("MISSING_KEY");
    }

    [Fact]
    public async Task EndpointValidator_ConstructsConfiguredProviderWithoutInference()
    {
        var secrets = new StubSecretProvider("secret");
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<ISecretProvider>(secrets);
        services.AddOllamaLlmProvider();
        services.AddLlmRouting(Configuration(secretName: "OLLAMA_KEY"));

        await using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<ILlmEndpointValidator>()
            .ValidateAsync(TestContext.Current.CancellationToken);

        report.Succeeded.Should().BeTrue();
        secrets.Requests.Should().Equal("OLLAMA_KEY");
    }

    private static IConfiguration Configuration(string? secretName = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["LlmRouting:Models:0:Name"] = "alias",
            ["LlmRouting:Models:0:Endpoints:0:Id"] = "ollama",
            ["LlmRouting:Models:0:Endpoints:0:Provider"] = "Ollama",
            ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] = "qwen",
            ["LlmRouting:Models:0:Endpoints:0:BaseUrl"] = "http://localhost:11434"
        };
        if (secretName is not null)
            values["LlmRouting:Models:0:Endpoints:0:ApiKeySecretName"] = secretName;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class StubSecretProvider(string? value) : ISecretProvider
    {
        public List<string> Requests { get; } = [];

        public Task<string?> GetSecretAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(name);
            return Task.FromResult(value);
        }
    }
}
