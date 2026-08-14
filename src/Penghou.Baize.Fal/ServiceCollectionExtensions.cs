using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Fal;

/// <summary>Dependency-injection registration for the fal.ai provider adapter.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a fal.ai queue artifact-generation endpoint as a keyed
    /// <see cref="IGenerationClient"/>. Multiple generation endpoints can be
    /// registered under distinct <paramref name="endpointId"/> values.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="configure">Configures the fal generation endpoint.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBaizeFalGeneration(
        this IServiceCollection services,
        string endpointId,
        Action<FalGenerationOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBaizeGeneration();
        services.Configure(endpointId, configure);
        services.AddKeyedSingleton<IGenerationClient>(endpointId, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<FalGenerationOptions>>()
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
            var client = new FalGenerationClient(
                options.Model,
                sp.GetRequiredService<IHttpClientFactory>(),
                options.ApiKey,
                options.BaseUrl,
                capabilities,
                endpointId);
            sp.GetRequiredService<IGenerationClientRegistry>()
                .Register("Fal", endpointId, client);
            return client;
        });
        return services;
    }
}