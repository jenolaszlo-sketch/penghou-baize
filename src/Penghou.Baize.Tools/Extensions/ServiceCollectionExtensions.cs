using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Penghou.Nuwa.Extensions;
using Penghou.Nuwa;

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
        => services.AddLlmTools(_ => { });

    /// <summary>Adds the tools pipeline with customized Nuwa repair options.</summary>
    public static IServiceCollection AddLlmTools(
        this IServiceCollection services,
        Action<JsonRepairOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddJsonRepair(configure);
        services.TryAddSingleton<IContentToolCallExtractor, ContentToolCallExtractor>();
        services.TryAddSingleton<ILlmResponseNormalizer, LlmResponseNormalizer>();
        services.TryAddSingleton<ILlmStructuredOutputRepairer, LlmStructuredOutputRepairer>();

        return services;
    }

    /// <summary>
    /// Adds deterministic structured-output repair to clients created by
    /// <c>AddLlmRouting</c>. Schema-constrained responses are buffered until
    /// they can be validated and repaired; other requests keep streaming.
    /// </summary>
    public static IServiceCollection AddLlmStructuredOutputRepair(
        this IServiceCollection services)
        => services.AddLlmStructuredOutputRepair(_ => { });

    /// <summary>
    /// Adds deterministic structured-output repair with an explicit streaming
    /// buffering policy.
    /// </summary>
    public static IServiceCollection AddLlmStructuredOutputRepair(
        this IServiceCollection services,
        Action<StructuredOutputRepairOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddLlmTools();
        var options = new StructuredOutputRepairOptions();
        configure(options);
        services.TryAddSingleton(options);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILlmClientDecorator,
                StructuredOutputRepairingLlmClientDecorator>());
        return services;
    }
}
