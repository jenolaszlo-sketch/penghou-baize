namespace Penghou.Baize.Router;

/// <summary>
/// Default, process-local implementation of <see cref="ILlmRouterMemory"/>.
/// Call counts and feature-reliability failures are cumulative; availability
/// failures are kept as timestamps and only counted within a sliding window.
/// History is keyed by endpoint id. The operations are synchronous under the
/// hood and complete their returned tasks immediately. Thread-safe.
/// </summary>
public sealed class InMemoryLlmRouterMemory : ILlmRouterMemory
{
    private readonly object _lock = new();
    private readonly Dictionary<string, EndpointState> _states = new();
    private readonly TimeSpan _availabilityWindow;

    /// <summary>Initializes an in-memory router memory.</summary>
    /// <param name="availabilityWindow">The sliding window for availability failures.</param>
    public InMemoryLlmRouterMemory(TimeSpan? availabilityWindow = null)
    {
        _availabilityWindow = availabilityWindow ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>Records that a call was made to an endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A task representing the record operation.</returns>
    public Task RecordCallAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            GetState(endpointId).TotalCalls++;
        }

        return Task.CompletedTask;
    }

    /// <summary>Records a failure for an endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="category">The category of failure.</param>
    /// <param name="unavailableUntil">
    /// The earliest instant the endpoint is expected to accept requests again,
    /// when the provider reported one. The latest reported value wins.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A task representing the record operation.</returns>
    public Task RecordFailureAsync(
        string endpointId,
        LlmFailureCategory category,
        DateTimeOffset? unavailableUntil = null,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var state = GetState(endpointId);

            // Drop expired availability timestamps as new failures arrive so
            // the sliding window stays bounded even if stats are never read.
            Prune(state);

            switch (category)
            {
                case LlmFailureCategory.Availability:
                    state.AvailabilityFailures.Add(DateTimeOffset.UtcNow);

                    if (unavailableUntil is { } blockedUntil &&
                        (state.UnavailableUntil is null ||
                         blockedUntil > state.UnavailableUntil))
                    {
                        state.UnavailableUntil = blockedUntil;
                    }

                    break;

                case LlmFailureCategory.ToolRepairNeeded:
                    state.ToolRepairFailures++;
                    break;

                case LlmFailureCategory.StructuredOutputMismatch:
                    state.StructuredOutputFailures++;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(category),
                        category,
                        null);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Returns the current recorded history for an endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The endpoint's current stats.</returns>
    public Task<LlmEndpointStats> GetStatsAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(GetStats(endpointId));

    /// <summary>Returns a synchronous snapshot of the recorded history for an endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <returns>The endpoint's current stats.</returns>
    public LlmEndpointStats GetStats(string endpointId)
    {
        lock (_lock)
        {
            var state = GetState(endpointId);
            Prune(state);

            return new LlmEndpointStats(
                EndpointId: endpointId,
                TotalCalls: state.TotalCalls,
                AvailabilityFailures: state.AvailabilityFailures.Count,
                ToolRepairFailures: state.ToolRepairFailures,
                StructuredOutputFailures: state.StructuredOutputFailures,
                UnavailableUntil: state.UnavailableUntil);
        }
    }

    private EndpointState GetState(string endpointId)
    {
        if (_states.TryGetValue(endpointId, out var state))
            return state;

        state = new EndpointState();
        _states[endpointId] = state;
        return state;
    }

    private void Prune(EndpointState state)
    {
        var cutoff = DateTimeOffset.UtcNow - _availabilityWindow;
        state.AvailabilityFailures.RemoveAll(
            timestamp => timestamp < cutoff);
    }

    private sealed class EndpointState
    {
        public long TotalCalls;
        public readonly List<DateTimeOffset> AvailabilityFailures = [];
        public long ToolRepairFailures;
        public long StructuredOutputFailures;
        public DateTimeOffset? UnavailableUntil;
    }
}
