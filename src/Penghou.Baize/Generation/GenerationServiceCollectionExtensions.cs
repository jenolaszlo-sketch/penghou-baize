using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Penghou.Baize.Generation;

/// <summary>
/// Materializes one configured generation endpoint into the
/// <see cref="IGenerationClientRegistry"/> when the registry is resolved.
/// Provider packages register descriptors instead of registering lazily from
/// inside keyed-service factories, so every configured endpoint is visible to
/// routing even when nothing has resolved its keyed client yet.
/// </summary>
public interface IGenerationEndpointDescriptor
{
    /// <summary>Builds the client from options and registers it.</summary>
    /// <param name="serviceProvider">The container resolving the registry.</param>
    /// <param name="registry">The registry being materialized.</param>
    void Register(
        IServiceProvider serviceProvider,
        IGenerationClientRegistry registry);
}

/// <summary>A descriptor backed by a delegate.</summary>
public sealed class DelegateGenerationEndpointDescriptor(
    Action<IServiceProvider, IGenerationClientRegistry> register)
    : IGenerationEndpointDescriptor
{
    /// <inheritdoc />
    public void Register(
        IServiceProvider serviceProvider,
        IGenerationClientRegistry registry) =>
        register(serviceProvider, registry);
}

/// <summary>Shared dependency-injection registration for generation infrastructure.</summary>
public static class GenerationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared generation infrastructure: the
    /// <see cref="IGenerationClientRegistry"/> used to reconstruct generation
    /// clients from persisted operation handles, the deterministic
    /// <see cref="IGenerationRoutingPolicy"/>, the in-process
    /// <see cref="IGenerationExecutor"/>, and the logical
    /// <see cref="IGenerationBatchExecutor"/>. Resolving the registry
    /// materializes every registered endpoint descriptor eagerly, so routing
    /// sees the full configured surface and malformed options fail at startup
    /// rather than on the first billable call. Safe to call multiple times.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureExecutor">Optional executor polling configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBaizeGeneration(
        this IServiceCollection services,
        Action<GenerationExecutorOptions>? configureExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<DefaultGenerationClientRegistry>();
        services.TryAddSingleton<IGenerationClientRegistry>(sp =>
        {
            var registry = sp.GetRequiredService<DefaultGenerationClientRegistry>();

            foreach (var descriptor in sp.GetServices<IGenerationEndpointDescriptor>())
            {
                descriptor.Register(sp, registry);
            }

            return registry;
        });
        services.TryAddSingleton<IGenerationRoutingPolicy, DefaultGenerationRoutingPolicy>();
        services.TryAddSingleton<IGenerationExecutor, GenerationExecutor>();
        services.TryAddSingleton<IGenerationBatchExecutor, GenerationBatchExecutor>();
        if (configureExecutor is not null)
            services.Configure(configureExecutor);
        return services;
    }
}
