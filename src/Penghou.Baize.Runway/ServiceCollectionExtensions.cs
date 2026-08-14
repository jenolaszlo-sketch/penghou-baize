using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Runway;

/// <summary>Dependency-injection registration for the Runway provider adapter.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Runway artifact-generation endpoint as a keyed
    /// <see cref="IGenerationClient"/>. Multiple generation endpoints can be
    /// registered under distinct <paramref name="endpointId"/> values.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="configure">Configures the Runway generation endpoint.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBaizeRunwayGeneration(
        this IServiceCollection services,
        string endpointId,
        Action<RunwayGenerationOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBaizeGeneration();
        services.Configure(endpointId, configure);
        services.AddKeyedSingleton<IGenerationClient>(endpointId, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<RunwayGenerationOptions>>()
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
            var client = new RunwayGenerationClient(
                options.Model,
                sp.GetRequiredService<IHttpClientFactory>(),
                options.ApiKey,
                new Uri(options.BaseUrl),
                capabilities,
                endpointId,
                options.ApiVersion,
                options.DefaultInputImageMimeType,
                options.DefaultRatio,
                options.DefaultOutputFormat);
            sp.GetRequiredService<IGenerationClientRegistry>()
                .Register("Runway", endpointId, client);
            return client;
        });
        return services;
    }
}