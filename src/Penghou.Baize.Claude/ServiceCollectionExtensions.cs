using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Penghou.Baize.Claude;

/// <summary>Dependency-injection registration for the Claude provider adapter.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Claude provider with the Baize router registry.</summary>
    public static IServiceCollection AddClaudeLlmProvider(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILlmClientProvider, ClaudeClientProvider>());
        return services;
    }
}
