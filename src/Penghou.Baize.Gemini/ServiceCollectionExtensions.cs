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
    /// registered under distinct <paramref name="endpointId"/> values.
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
        services.AddKeyedSingleton<IGenerationClient>(endpointId, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<GeminiGenerationOptions>>()
                .Get(endpointId);
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
                sp.GetRequiredService<IHttpClientFactory>(),
                options.ApiKey,
                options.BaseUrl,
                capabilities,
                endpointId,
                options.ImageSize,
                options.DefaultInputImageMimeType,
                options.StoreResponses);
            sp.GetRequiredService<IGenerationClientRegistry>()
                .Register("Gemini", endpointId, client);
            return client;
        });
        return services;
    }
}
