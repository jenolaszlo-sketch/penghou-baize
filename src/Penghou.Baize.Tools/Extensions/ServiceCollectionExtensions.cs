using Microsoft.Extensions.DependencyInjection;
using Penghou.Nuwa.Extensions;

namespace Penghou.Baize.Tools.Extensions;

/// <summary>
/// Registers the LlmTools pipeline services with the dependency injection
/// container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the content tool-call extraction and response normalization
    /// services (and their JSON-repair dependencies) as singletons.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddLlmTools(this IServiceCollection services)
    {
        services.AddJsonRepair();
        services.AddSingleton<IContentToolCallExtractor, ContentToolCallExtractor>();
        services.AddSingleton<ILlmResponseNormalizer, LlmResponseNormalizer>();
        services.AddSingleton<ILlmStructuredOutputRepairer, LlmStructuredOutputRepairer>();

        return services;
    }
}
