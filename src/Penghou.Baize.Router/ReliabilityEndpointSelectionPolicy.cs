namespace Penghou.Baize.Router;

/// <summary>
/// Default policy that skips active cooldowns, then ranks by recent
/// availability failures and cumulative quality-failure rate.
/// </summary>
public sealed class ReliabilityEndpointSelectionPolicy : ILlmEndpointSelectionPolicy
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ResolvedEndpoint>> OrderAsync(
        IReadOnlyList<ResolvedEndpoint> candidates,
        LlmRequest? request,
        ModelStrategy strategy,
        ILlmRouterMemory memory,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var ranked = new List<(ResolvedEndpoint Endpoint, LlmEndpointStats Stats)>();

        foreach (var endpoint in candidates)
        {
            var stats = await memory.GetStatsAsync(
                endpoint.EndpointId,
                cancellationToken);
            ranked.Add((endpoint, stats));
        }

        var available = ranked
            .Where(candidate => candidate.Stats.UnavailableUntil is null ||
                candidate.Stats.UnavailableUntil <= now)
            .ToList();
        var pool = available.Count > 0 ? available : ranked;

        return pool
            .OrderBy(candidate => candidate.Stats.AvailabilityFailures)
            .ThenBy(candidate => QualityFailureRate(candidate.Stats))
            .Select(candidate => candidate.Endpoint)
            .ToArray();
    }

    private static double QualityFailureRate(LlmEndpointStats stats) =>
        stats.TotalCalls == 0
            ? 0
            : (stats.ToolRepairFailures + stats.StructuredOutputFailures) /
              (double)stats.TotalCalls;
}
