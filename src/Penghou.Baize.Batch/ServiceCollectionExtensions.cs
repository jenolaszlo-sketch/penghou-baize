using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Penghou.Baize.Router;

namespace Penghou.Baize.Batch;

/// <summary>Dependency-injection registration for native batch planning.</summary>
public static class BatchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the batch-client resolver and planner over the endpoint lookup
    /// installed by <c>AddLlmRouting</c>.
    /// </summary>
    public static IServiceCollection AddBaizeBatch(
        this IServiceCollection services,
        BatchPlannerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var plannerOptions = options ?? new BatchPlannerOptions();

        if (plannerOptions.MaxItemsPerGroup is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                plannerOptions.MaxItemsPerGroup,
                "MaxItemsPerGroup must be greater than zero when specified.");
        }

        services.TryAddSingleton<IBaizeBatchClientResolver,
            ModelLookupBatchClientResolver>();
        services.TryAddSingleton<IBaizeBatchPlanner>(provider =>
            new BatchPlanner(
                provider.GetRequiredService<ILlmModelLookup>(),
                provider.GetRequiredService<IBaizeBatchClientResolver>(),
                plannerOptions));
        services.TryAddSingleton<IBaizeBatchCoordinator, BaizeBatchCoordinator>();

        return services;
    }
}
