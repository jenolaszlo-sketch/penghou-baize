using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Penghou.Baize.Generation;

/// <summary>Shared dependency-injection registration for generation infrastructure.</summary>
public static class GenerationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared generation infrastructure: the
    /// <see cref="IGenerationClientRegistry"/> used to reconstruct generation
    /// clients from persisted operation handles, the deterministic
    /// <see cref="IGenerationRoutingPolicy"/>, the in-process
    /// <see cref="IGenerationExecutor"/>, and the logical
    /// <see cref="IGenerationBatchExecutor"/>. Safe to call multiple times.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureExecutor">Optional executor polling configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBaizeGeneration(
        this IServiceCollection services,
        Action<GenerationExecutorOptions>? configureExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IGenerationClientRegistry, DefaultGenerationClientRegistry>();
        services.TryAddSingleton<IGenerationRoutingPolicy, DefaultGenerationRoutingPolicy>();
        services.TryAddSingleton<IGenerationExecutor, GenerationExecutor>();
        services.TryAddSingleton<IGenerationBatchExecutor, GenerationBatchExecutor>();
        if (configureExecutor is not null)
            services.Configure(configureExecutor);
        return services;
    }
}