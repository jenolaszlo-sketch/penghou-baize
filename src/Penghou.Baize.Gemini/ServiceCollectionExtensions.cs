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
    public static IServiceCollection AddBaizeGeminiGeneration(
        this IServiceCollection services,
        string endpointId,
        Action<GeminiGenerationOptions> configure)
    {
        return services.AddBaizeGenerationEndpoint<GeminiGenerationOptions>(
            "Gemini",
            endpointId,
            configure,
            (sp, options) => ValidateEndpointOptions(endpointId, options),
            (sp, options) =>
            {
                var capabilities = new GenerationCapabilities
                {
                    Features = options.Features,
                    InputTransports = new HashSet<LlmContentTransport>
                    {
                        LlmContentTransport.Uri,
                        LlmContentTransport.InlineData
                    }
                };
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                if (options.RequestTimeout is { } requestTimeout)
                {
                    httpClientFactory = httpClientFactory.WithRequestTimeout(requestTimeout);
                }
                return new GeminiGenerationClient(
                    options.Model,
                    httpClientFactory,
                    options.ApiKey,
                    options.BaseUrl,
                    capabilities,
                    endpointId,
                    options.ImageSize,
                    options.DefaultInputImageMimeType,
                    options.StoreResponses);
            });
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
