using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Gemini;

/// <summary>Dependency-injection registration for the Gemini provider adapter.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Gemini provider with the Baize router registry.</summary>
    public static IServiceCollection AddGeminiLlmProvider(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILlmClientProvider, GeminiClientProvider>());
        return services;
    }

    /// <summary>
    /// Registers a Gemini artifact-generation endpoint as a keyed
    /// <see cref="IGenerationClient"/>. Multiple generation endpoints can be
    /// registered under distinct <paramref name="endpointId"/> values. Endpoint
    /// options are validated and the client is registered with routing when
    /// the <see cref="IGenerationClientRegistry"/> is resolved — not lazily on
    /// first use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="configure">Configures the Gemini generation endpoint.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBaizeGeminiGeneration(
        this IServiceCollection services,
        string endpointId,
        Action<GeminiGenerationOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBaizeGeneration();
        services.Configure(endpointId, configure);
        services.AddSingleton<IGenerationEndpointDescriptor>(
            new DelegateGenerationEndpointDescriptor((sp, registry) =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<GeminiGenerationOptions>>()
                    .Get(endpointId);
                ValidateEndpointOptions(endpointId, options);

                // Per-model timeout: wrap once so every call this client makes enforces it.
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                if (options.RequestTimeout is { } requestTimeout)
                {
                    httpClientFactory = httpClientFactory.WithRequestTimeout(requestTimeout);
                }

                var capabilities = new GenerationCapabilities
                {
                    Features = options.Features,
                    InputTransports = new HashSet<LlmContentTransport>
                    {
                        LlmContentTransport.Uri,
                        LlmContentTransport.InlineData
                    }
                };
                var client = new GeminiGenerationClient(
                    options.Model,
                    httpClientFactory,
                    options.ApiKey,
                    options.BaseUrl,
                    capabilities,
                    endpointId,
                    options.ImageSize,
                    options.DefaultInputImageMimeType,
                    options.StoreResponses);
                registry.Register("Gemini", endpointId, client);
            }));
        services.AddKeyedSingleton<IGenerationClient>(endpointId, (sp, _) =>
            ResolveRegisteredClient(sp, "Gemini", endpointId));
        return services;
    }

    internal static void ValidateEndpointOptions(
        string endpointId,
        GeminiGenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException(
                $"Gemini generation endpoint '{endpointId}' requires a Model.");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException(
                $"Gemini generation endpoint '{endpointId}' requires an ApiKey.");
    }

    private static IGenerationClient ResolveRegisteredClient(
        IServiceProvider sp,
        string provider,
        string endpointId)
    {
        var registry = sp.GetRequiredService<IGenerationClientRegistry>();
        return registry.Endpoints.First(endpoint =>
                string.Equals(endpoint.EndpointId, endpointId, StringComparison.Ordinal) &&
                string.Equals(endpoint.Provider, provider, StringComparison.Ordinal))
            .Client;
    }
}
