using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
}
