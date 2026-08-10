using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
}
