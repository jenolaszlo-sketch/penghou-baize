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
    /// registered under distinct <paramref name="endpointId"/> values. Endpoint
    /// options are validated and the client is registered with routing when
    /// the <see cref="IGenerationClientRegistry"/> is resolved — not lazily on
    /// first use.
    /// </summary>
    public static IServiceCollection AddBaizeFalGeneration(
        this IServiceCollection services,
        string endpointId,
        Action<FalGenerationOptions> configure)
    {
        return services.AddBaizeGenerationEndpoint<FalGenerationOptions>(
            "Fal",
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
                return new FalGenerationClient(
                    options.Model,
                    httpClientFactory,
                    options.ApiKey,
                    options.BaseUrl,
                    capabilities,
                    endpointId);
            });
    }
    internal static void ValidateEndpointOptions(
        string endpointId,
        FalGenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException(
                $"fal generation endpoint '{endpointId}' requires a Model.");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException(
                $"fal generation endpoint '{endpointId}' requires an ApiKey.");
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