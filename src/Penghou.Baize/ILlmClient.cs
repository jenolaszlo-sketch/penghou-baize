namespace Penghou.Baize;

/// <summary>
/// A provider-specific chat client that exposes a canonical streaming event
/// stream.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// The declared capabilities of the endpoint this client talks to.
    /// Queryable so callers can adapt their prompting strategy to what the
    /// endpoint actually supports (for example, splitting a request into
    /// several calls when parallel tool calling is unavailable).
    /// </summary>
    LlmEndpointCapabilities Capabilities { get; }

    /// <summary>
    /// Streams the completion for <paramref name="request"/> as canonical
    /// <see cref="LlmStreamEvent"/>s.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default);
}
