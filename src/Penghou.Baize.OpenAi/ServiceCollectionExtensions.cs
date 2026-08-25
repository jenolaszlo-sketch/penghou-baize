using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;

namespace Penghou.Baize.OpenAi;

/// <summary>Dependency-injection registration for the OpenAI provider adapter.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the OpenAI-compatible provider with the Baize router registry.</summary>
    public static IServiceCollection AddOpenAiLlmProvider(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILlmClientProvider, OpenAiClientProvider>());
        return services;
    }

    /// <summary>
    /// Registers an OpenAI artifact-generation endpoint as a keyed
    /// <see cref="IGenerationClient"/>. Multiple generation endpoints can be
    /// registered under distinct <paramref name="endpointId"/> values.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="configure">Configures the OpenAI generation endpoint.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBaizeOpenAiGeneration(
        this IServiceCollection services,
        string endpointId,
        Action<OpenAiGenerationOptions> configure)
    {
        return services.AddBaizeGenerationEndpoint<OpenAiGenerationOptions>(
            "OpenAi",
            endpointId,
            configure,
            validate: null,
            (sp, options) =>
            {
                var capabilities = BuildCapabilities(options.Features, options.MaximumCandidates);

                // Per-model timeout: wrap once so every call this client makes enforces it.
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                if (options.RequestTimeout is { } requestTimeout)
                {
                    httpClientFactory = httpClientFactory.WithRequestTimeout(requestTimeout);
                }

                return new OpenAiGenerationClient(
                    options.Model,
                    httpClientFactory,
                    options.ApiKey,
                    options.BaseAddress,
                    capabilities,
                    endpointId,
                    options.ImageModel,
                    options.VideoModel,
                    options.AudioModel,
                    options.DefaultVoice);
            });
    }

    /// <summary>
    /// Registers an opt-in OpenAI-compatible artifact-generation endpoint.
    /// Generation is never inferred from OpenAI-compatible chat support; only
    /// the explicitly configured <see cref="OpenAiCompatibleGenerationOptions.Features"/>
    /// are advertised. Endpoint options are validated and the client is
    /// registered with routing when the <see cref="IGenerationClientRegistry"/>
    /// is resolved—not lazily on first use.
    /// </summary>
    public static IServiceCollection AddBaizeOpenAiCompatibleGeneration(
        this IServiceCollection services,
        string endpointId,
        Action<OpenAiCompatibleGenerationOptions> configure)
    {
        return services.AddBaizeGenerationEndpoint<OpenAiCompatibleGenerationOptions>(
            "OpenAi",
            endpointId,
            configure,
            (sp, options) => ValidateEndpointOptions(endpointId, options),
            (sp, options) =>
            {
                var capabilities = BuildCapabilities(options.Features, options.MaximumCandidates);
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                if (options.RequestTimeout is { } requestTimeout)
                {
                    httpClientFactory = httpClientFactory.WithRequestTimeout(requestTimeout);
                }
                return new OpenAiGenerationClient(
                    options.Model,
                    httpClientFactory,
                    options.ApiKey,
                    options.BaseAddress,
                    capabilities,
                    endpointId,
                    options.ImageModel);
            });
    }
    internal static void ValidateEndpointOptions(
        string endpointId,
        OpenAiCompatibleGenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException(
                $"OpenAI generation endpoint '{endpointId}' requires a Model.");
    }


    private static GenerationCapabilities BuildCapabilities(
        GenerationFeature features,
        int? maximumCandidates) =>
        new()
        {
            Features = features,
            InputTransports = new HashSet<LlmContentTransport>
            {
                LlmContentTransport.Uri,
                LlmContentTransport.InlineData
            },
            MaximumCandidates = maximumCandidates
        };
}
