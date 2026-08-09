namespace Penghou.Baize.Router;

/// <summary>
/// A snapshot of the recorded call and failure history for a single endpoint.
/// Availability failures reflect a sliding window; the other counters are
/// cumulative since the memory started tracking the endpoint.
/// </summary>
/// <param name="EndpointId">The endpoint's stable id.</param>
/// <param name="TotalCalls">The cumulative number of recorded calls.</param>
/// <param name="AvailabilityFailures">The number of availability failures inside the sliding window.</param>
/// <param name="ToolRepairFailures">The cumulative number of tool-repair failures.</param>
/// <param name="StructuredOutputFailures">The cumulative number of structured-output mismatches.</param>
/// <param name="UnavailableUntil">The earliest instant the endpoint is expected to accept requests again, when a cooldown is active.</param>
public sealed record LlmEndpointStats(
    string EndpointId,
    long TotalCalls,
    long AvailabilityFailures,
    long ToolRepairFailures,
    long StructuredOutputFailures,
    DateTimeOffset? UnavailableUntil = null);
