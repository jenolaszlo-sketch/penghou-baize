using Penghou.Baize.Router;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;
using FluentAssertions;

namespace Penghou.Baize.Batch.Tests;

public sealed class BatchCapabilityOverlayTests
{
    [Fact]
    public void ResolveCapabilities_DefaultsToProviderBatchCapability()
    {
        var endpoint = new LlmEndpointOptions { ApiStyle = ApiStyle.OpenAi };
        var provider = new StubProvider(BatchCapabilities.NativeBatch);

        var capabilities = ServiceCollectionExtensions.ResolveCapabilities(
            endpoint,
            profiles: new Dictionary<string, LlmEndpointCapabilitiesOptions>(),
            provider);

        capabilities.Batch.Should().Be(BatchCapabilities.NativeBatch);
    }

    [Fact]
    public void ResolveCapabilities_ConservativeDefaultIsNone()
    {
        var endpoint = new LlmEndpointOptions { ApiStyle = ApiStyle.OpenAi };
        var provider = new StubProvider(BatchCapabilities.None);

        var capabilities = ServiceCollectionExtensions.ResolveCapabilities(
            endpoint,
            profiles: new Dictionary<string, LlmEndpointCapabilitiesOptions>(),
            provider);

        capabilities.Batch.Should().Be(BatchCapabilities.None);
    }

    [Fact]
    public void ResolveCapabilities_EndpointOverrideWins()
    {
        var endpoint = new LlmEndpointOptions
        {
            ApiStyle = ApiStyle.OpenAi,
            Capabilities = new LlmEndpointCapabilitiesOptions
            {
                Batch = BatchCapabilities.Polling
            }
        };
        var provider = new StubProvider(BatchCapabilities.NativeBatch);

        var capabilities = ServiceCollectionExtensions.ResolveCapabilities(
            endpoint,
            profiles: new Dictionary<string, LlmEndpointCapabilitiesOptions>(),
            provider);

        capabilities.Batch.Should().Be(BatchCapabilities.Polling);
    }

    [Fact]
    public void ResolveCapabilities_ProfileBatchApplied()
    {
        var endpoint = new LlmEndpointOptions
        {
            ApiStyle = ApiStyle.OpenAi,
            Profile = "batched"
        };
        var provider = new StubProvider(BatchCapabilities.None);
        var profiles = new Dictionary<string, LlmEndpointCapabilitiesOptions>
        {
            ["batched"] = new()
            {
                Batch = BatchCapabilities.NativeBatch | BatchCapabilities.Cancellation
            }
        };

        var capabilities = ServiceCollectionExtensions.ResolveCapabilities(
            endpoint,
            profiles,
            provider);

        capabilities.Batch.Should().Be(
            BatchCapabilities.NativeBatch | BatchCapabilities.Cancellation);
    }

    [Fact]
    public void ResolveCapabilities_EndpointOverridesProfile()
    {
        var endpoint = new LlmEndpointOptions
        {
            ApiStyle = ApiStyle.OpenAi,
            Profile = "batched",
            Capabilities = new LlmEndpointCapabilitiesOptions
            {
                Batch = BatchCapabilities.Polling
            }
        };
        var provider = new StubProvider(BatchCapabilities.None);
        var profiles = new Dictionary<string, LlmEndpointCapabilitiesOptions>
        {
            ["batched"] = new() { Batch = BatchCapabilities.NativeBatch }
        };

        var capabilities = ServiceCollectionExtensions.ResolveCapabilities(
            endpoint,
            profiles,
            provider);

        capabilities.Batch.Should().Be(BatchCapabilities.Polling);
    }

    private sealed class StubProvider(BatchCapabilities batch) : ILlmClientProvider
    {
        public LlmProviderKey Key => new("Stub");
        public string DefaultBaseUrl => "http://localhost";
        public LlmEndpointCapabilities DefaultCapabilities { get; } =
            new() { Batch = batch };

        public ILlmClient CreateClient(LlmClientProviderContext context) =>
            new StubChatClient();
    }

    private sealed class StubChatClient : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
