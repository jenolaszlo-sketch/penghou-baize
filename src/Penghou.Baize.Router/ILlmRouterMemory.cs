namespace Penghou.Baize.Router;

/// <summary>
/// Records per-endpoint call and failure history used by the router to pick
/// the least-failing endpoint and to let applications report quality events
/// (tool repair, structured-output mismatches) discovered after the fact.
///
/// History is keyed by the endpoint's stable id (see
/// <see cref="ResolvedEndpoint.EndpointId"/>), so two endpoints of the same
/// logical model — for example a primary and a backup gateway — keep separate
/// stats and cooldowns.
///
/// The interface is async so durable or shared backings (Redis, a database,
/// ...) can be swapped in without changing the router. The package ships an
/// in-memory implementation (<see cref="InMemoryLlmRouterMemory"/>);
/// applications can register their own implementation on the service
/// collection after <c>AddLlmRouting</c>.
/// </summary>
public interface ILlmRouterMemory
{
    /// <summary>Records that a call was made to an endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A task representing the record operation.</returns>
    Task RecordCallAsync(
        string endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>Records a failure for an endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="category">The category of failure.</param>
    /// <param name="unavailableUntil">
    /// The earliest instant the endpoint is expected to accept requests again,
    /// when the provider reported one (for example a rate-limit reset or
    /// retry hint). Drives skipping the endpoint during selection until the
    /// cooldown expires.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A task representing the record operation.</returns>
    Task RecordFailureAsync(
        string endpointId,
        LlmFailureCategory category,
        DateTimeOffset? unavailableUntil = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current recorded history for an endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The endpoint's current stats.</returns>
    Task<LlmEndpointStats> GetStatsAsync(
        string endpointId,
        CancellationToken cancellationToken = default);
}
