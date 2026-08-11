namespace Penghou.Baize.Router;

/// <summary>
/// Optional base for route providers that use Baize routing memory. Direct
/// <see cref="ILlmRouteProvider"/> implementations remain fully supported.
/// </summary>
public abstract class LlmRouteProviderBase(ILlmRouterMemory memory) : ILlmRouteProvider
{
    /// <summary>The independently replaceable routing-memory implementation.</summary>
    protected ILlmRouterMemory Memory { get; } = memory ??
        throw new ArgumentNullException(nameof(memory));

    /// <summary>Returns the recorded endpoint statistics.</summary>
    protected Task<LlmEndpointStats> GetStatsAsync(
        ResolvedEndpoint endpoint,
        CancellationToken cancellationToken = default) =>
        Memory.GetStatsAsync(endpoint.EndpointId, cancellationToken);

    /// <summary>Whether the supplied statistics indicate an active cooldown.</summary>
    protected static bool IsCoolingDown(
        LlmEndpointStats stats,
        DateTimeOffset now) =>
        stats.UnavailableUntil is { } until && until > now;

    /// <inheritdoc />
    public abstract ValueTask<LlmRouteResolution> ResolveAsync(
        LlmRoutingContext context,
        CancellationToken cancellationToken = default);
}
