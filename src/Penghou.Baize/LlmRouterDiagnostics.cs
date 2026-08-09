namespace Penghou.Baize;

/// <summary>
/// Routing-level diagnostics describing the endpoint attempts the router made
/// for a single stream. The router emits this as the final event of a
/// successful stream, and as the event immediately preceding the exception
/// when a stream fails.
/// </summary>
/// <param name="Attempts">The endpoint attempts in the order they were made.</param>
public sealed record LlmRouterDiagnostics(
    IReadOnlyList<LlmRouterAttempt> Attempts);

/// <summary>
/// Describes how an individual endpoint attempt for a routed stream ended.
/// </summary>
/// <param name="EndpointId">The endpoint's stable id.</param>
/// <param name="EndpointModel">The registration name of the endpoint's logical model.</param>
/// <param name="EndpointApiStyle">The name of the endpoint's API style (wire protocol).</param>
/// <param name="Outcome">Whether the attempt served the stream or failed.</param>
/// <param name="Duration">The elapsed time of the attempt.</param>
/// <param name="Error">The failure message, when the attempt failed.</param>
/// <param name="UnavailableUntil">
/// The cooldown recorded for the endpoint, when the failure reported one
/// (for example a rate-limit reset or retry hint).
/// </param>
public sealed record LlmRouterAttempt(
    string EndpointId,
    string EndpointModel,
    string EndpointApiStyle,
    LlmRouterAttemptOutcome Outcome,
    TimeSpan Duration,
    string? Error = null,
    DateTimeOffset? UnavailableUntil = null);

/// <summary>How an endpoint attempt for a routed stream ended.</summary>
public enum LlmRouterAttemptOutcome
{
    /// <summary>The attempt served the stream to completion.</summary>
    Succeeded,

    /// <summary>
    /// The attempt ended in an availability failure (a connection error, a
    /// timeout, or a provider-reported 429 or 5xx response).
    /// </summary>
    Failed
}
