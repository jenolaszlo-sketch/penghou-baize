using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Penghou.Baize.Ollama;

/// <summary>Dependency-injection registration for the Ollama provider adapter.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Ollama provider with the Baize router registry.</summary>
    public static IServiceCollection AddOllamaLlmProvider(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILlmClientProvider, OllamaClientProvider>());
        return services;
    }
}
