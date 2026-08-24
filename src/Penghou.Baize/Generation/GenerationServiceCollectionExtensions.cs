using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Registers one configured generation endpoint for an arbitrary provider
    /// and options type. Endpoint options are validated by
    /// <paramref name="validate"/> and the client produced by
    /// <paramref name="createClient"/> is registered with routing when the
    /// <see cref="IGenerationClientRegistry"/> is resolved — not lazily on
    /// first use.
    /// </summary>
    /// <typeparam name="TOptions">The provider's endpoint options type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="providerName">The registry provider key (for example "Runway").</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="configure">Configures the endpoint options.</param>
    /// <param name="validate">Validates materialized options; throw to fail startup.</param>
    /// <param name="createClient">Builds the client from resolved options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBaizeGenerationEndpoint<TOptions>(
        this IServiceCollection services,
        string providerName,
        string endpointId,
        Action<TOptions> configure,
        Action<IServiceProvider, TOptions>? validate,
        Func<IServiceProvider, TOptions, IGenerationClient> createClient)
        where TOptions : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(createClient);

        services.AddBaizeGeneration();
        services.Configure(endpointId, configure);
        services.AddSingleton<IGenerationEndpointDescriptor>(
            new DelegateGenerationEndpointDescriptor((sp, registry) =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<TOptions>>()
                    .Get(endpointId);
                validate?.Invoke(sp, options);
                registry.Register(providerName, endpointId, createClient(sp, options));
            }));
        services.AddKeyedSingleton<IGenerationClient>(endpointId, (sp, _) =>
            ResolveRegisteredEndpoint(sp, providerName, endpointId));
        return services;
    }

    private static IGenerationClient ResolveRegisteredEndpoint(
        IServiceProvider serviceProvider,
        string providerName,
        string endpointId)
    {
        var registry = serviceProvider.GetRequiredService<IGenerationClientRegistry>();
        return registry.Endpoints.First(endpoint =>
                string.Equals(endpoint.EndpointId, endpointId, StringComparison.Ordinal) &&
                string.Equals(endpoint.Provider, providerName, StringComparison.Ordinal))
            .Client;
    }
}
