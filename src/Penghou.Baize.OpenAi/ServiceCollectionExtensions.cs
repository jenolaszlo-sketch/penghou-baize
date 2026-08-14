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
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBaizeGeneration();
        services.Configure(endpointId, configure);
        services.AddKeyedSingleton<IGenerationClient>(endpointId, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<OpenAiGenerationOptions>>()
                .Get(endpointId);
            var capabilities = BuildCapabilities(options.Features, options.MaximumCandidates);
            var client = new OpenAiGenerationClient(
                options.Model,
                sp.GetRequiredService<IHttpClientFactory>(),
                options.ApiKey,
                options.BaseAddress,
                capabilities,
                endpointId,
                options.ImageModel,
                options.VideoModel,
                options.AudioModel,
                options.DefaultVoice);
            sp.GetRequiredService<IGenerationClientRegistry>()
                .Register("OpenAi", endpointId, client);
            return client;
        });
        return services;
    }

    /// <summary>
    /// Registers an opt-in OpenAI-compatible artifact-generation endpoint.
    /// Generation is never inferred from OpenAI-compatible chat support; only
    /// the explicitly configured <see cref="OpenAiCompatibleGenerationOptions.Features"/>
    /// are advertised.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="configure">Configures the OpenAI-compatible generation endpoint.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBaizeOpenAiCompatibleGeneration(
        this IServiceCollection services,
        string endpointId,
        Action<OpenAiCompatibleGenerationOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBaizeGeneration();
        services.Configure(endpointId, configure);
        services.AddKeyedSingleton<IGenerationClient>(endpointId, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<OpenAiCompatibleGenerationOptions>>()
                .Get(endpointId);
            var capabilities = BuildCapabilities(options.Features, options.MaximumCandidates);
            var client = new OpenAiGenerationClient(
                options.Model,
                sp.GetRequiredService<IHttpClientFactory>(),
                options.ApiKey,
                options.BaseAddress,
                capabilities,
                endpointId,
                options.ImageModel);
            sp.GetRequiredService<IGenerationClientRegistry>()
                .Register("OpenAi", endpointId, client);
            return client;
        });
        return services;
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
