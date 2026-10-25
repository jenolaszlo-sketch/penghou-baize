using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;
using Penghou.Http.Abstractions;

namespace Penghou.Baize.Qualification.Planning;

// An observable denial port, not a transport guard or execution subsystem.
internal sealed class OfflineBoundary : ISecretProvider, IHttpTransport, IHttpClientFactory
{
    public int SecretReads { get; private set; }
    public int HttpSends { get; private set; }
    public int HttpClients { get; private set; }
    public int ProviderConstructions { get; private set; }
    public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        SecretReads++;
        throw new PlanningException("offline-secret-denied");
    }
    public ValueTask<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken = default)
    {
        HttpSends++;
        throw new PlanningException("offline-network-denied");
    }
    public HttpClient CreateClient(string name)
    {
        HttpClients++;
        throw new PlanningException("offline-http-construction-denied");
    }
    public ILlmClient Construct()
    {
        ProviderConstructions++;
        throw new PlanningException("offline-provider-construction-denied");
    }
}

internal static class OfflineResolution
{
    public static ILlmClientProvider Provider(string? name) => name?.ToUpperInvariant() switch
    {
        "OPENAI" => new OpenAi.OpenAiClientProvider(),
        "CLAUDE" => new Claude.ClaudeClientProvider(),
        "GEMINI" => new Gemini.GeminiClientProvider(),
        "OLLAMA" => new Ollama.OllamaClientProvider(),
        _ => throw new PlanningException("unsupported-provider")
    };

    public static IReadOnlyDictionary<string, LlmEndpointCapabilities> Resolve(Inventory inventory, OfflineBoundary boundary)
    {
        var options = new LlmRoutingOptions
        {
            Models = inventory.Entries.Select(e => new LlmModelOptions { Name = e.Id, Endpoints = [e.Endpoint] }).ToList(),
            Profiles = inventory.Profiles,
            MaxPendingRequests = 1,
            Retry = new LlmRouterRetryOptions { MaximumAttempts = 1 }
        };
        var services = new ServiceCollection();
        services.AddSingleton<ISecretProvider>(boundary);
        services.AddSingleton<IHttpTransport>(boundary);
        services.AddSingleton<IHttpClientFactory>(boundary);
        foreach (var provider in inventory.Entries.Select(e => Provider(e.Endpoint.Provider)).DistinctBy(p => p.Key))
            services.AddSingleton<ILlmClientProvider>(new MetadataProvider(provider, boundary));
        services.AddSingleton<IOptionsMonitor<LlmRoutingOptions>>(new FixedOptions(options));
        using var json = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { LlmRouting = options }));
        var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        services.AddLlmRouting(configuration);
        using var container = services.BuildServiceProvider();
        var lookup = container.GetRequiredService<ILlmModelLookup>();
        return inventory.Entries.ToDictionary(e => e.Id, e =>
        {
            var endpoints = lookup.GetEndpoints(e.Id);
            if (endpoints.Count != 1 || endpoints[0].EndpointId != e.Endpoint.Id)
                throw new PlanningException("ambiguous-endpoint");
            // This returns the metadata-bearing deferred shell, never a native client.
            return lookup.GetClientByEndpointId(e.Endpoint.Id!).Capabilities;
        }, StringComparer.Ordinal);
    }

    private sealed class FixedOptions(LlmRoutingOptions options) : IOptionsMonitor<LlmRoutingOptions>
    {
        public LlmRoutingOptions CurrentValue => options;
        public LlmRoutingOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<LlmRoutingOptions, string?> listener) => null;
    }
    private sealed class MetadataProvider(ILlmClientProvider inner, OfflineBoundary boundary) : ILlmClientProvider
    {
        public LlmProviderKey Key => inner.Key;
        public string DefaultBaseUrl => inner.DefaultBaseUrl;
        public LlmEndpointCapabilities DefaultCapabilities => inner.DefaultCapabilities;
        public ILlmClient CreateClient(LlmClientProviderContext context) => boundary.Construct();
        public IBaizeBatchClient? CreateBatchClient(LlmClientProviderContext context)
        {
            boundary.Construct();
            return null;
        }
    }
}
