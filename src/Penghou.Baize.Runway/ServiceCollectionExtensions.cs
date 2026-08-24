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
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBaizeGeneration();
        services.Configure(endpointId, configure);
        services.AddSingleton<IGenerationEndpointDescriptor>(
            new DelegateGenerationEndpointDescriptor((sp, registry) =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<RunwayGenerationOptions>>()
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
                        LlmContentTransport.InlineData,
                        LlmContentTransport.ProviderFile
                    }
                };
                var client = new RunwayGenerationClient(
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
                registry.Register("Runway", endpointId, client);
            }));
        services.AddKeyedSingleton<IGenerationClient>(endpointId, (sp, _) =>
            ResolveRegisteredClient(sp, "Runway", endpointId));
        return services;
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

    internal static IGenerationClient ResolveRegisteredClient(
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
