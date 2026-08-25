using Penghou.Baize.Generation;

namespace Penghou.Baize.Router;

/// <summary>
/// Bridges generation endpoint ordering to the router's shared reliability
/// memory, including cooldowns and recent availability failures.
/// </summary>
public sealed class RouterGenerationEndpointOrderer(
    ILlmRouterMemory memory) : IGenerationEndpointOrderer
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<GenerationEndpoint>> OrderAsync(
        IReadOnlyList<GenerationEndpoint> candidates,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var ranked = new List<(GenerationEndpoint Endpoint, LlmEndpointStats Stats)>();
        foreach (var endpoint in candidates)
        {
            ranked.Add((
                endpoint,
                await memory.GetStatsAsync(endpoint.EndpointId, cancellationToken)
                    .ConfigureAwait(false)));
        }

        var available = ranked.Where(item =>
            item.Stats.UnavailableUntil is null ||
            item.Stats.UnavailableUntil <= now).ToArray();
        IEnumerable<(GenerationEndpoint Endpoint, LlmEndpointStats Stats)> pool =
            available.Length > 0 ? available : ranked;
        return pool
            .OrderBy(item => item.Stats.AvailabilityFailures)
            .ThenBy(item => QualityFailureRate(item.Stats))
            .Select(item => item.Endpoint)
            .ToArray();
    }

    private static double QualityFailureRate(LlmEndpointStats stats) =>
        stats.TotalCalls == 0
            ? 0
            : (stats.ToolRepairFailures + stats.StructuredOutputFailures) /
              (double)stats.TotalCalls;
}
