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
    /// registered under distinct <paramref name="endpointId"/> values. Endpoint
    /// options are validated and the client is registered with routing when
    /// the <see cref="IGenerationClientRegistry"/> is resolved — not lazily on
    /// first use.
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
        return services.AddBaizeGenerationEndpoint<RunwayGenerationOptions>(
            "Runway",
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
                        LlmContentTransport.InlineData,
                        LlmContentTransport.ProviderFile
                    }
                };
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                if (options.RequestTimeout is { } requestTimeout)
                {
                    httpClientFactory = httpClientFactory.WithRequestTimeout(requestTimeout);
                }
                return new RunwayGenerationClient(
                    options.Model,
                    httpClientFactory,
                    options.ApiKey,
                    new Uri(options.BaseUrl),
                    capabilities,
                    endpointId,
                    options.ApiVersion,
                    options.DefaultInputImageMimeType,
                    options.DefaultRatio,
                    options.DefaultOutputFormat);
            });
    }
    internal static void ValidateEndpointOptions(
        string endpointId,
        RunwayGenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException(
                $"Runway generation endpoint '{endpointId}' requires a Model.");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException(
                $"Runway generation endpoint '{endpointId}' requires an ApiKey.");
    }

}
