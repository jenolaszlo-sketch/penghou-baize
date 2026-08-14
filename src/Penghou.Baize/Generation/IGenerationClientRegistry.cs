namespace Penghou.Baize.Generation;

/// <summary>
/// Looks up a configured generation client from a pinned operation handle so a
/// queued operation can be reconstructed and polled later. Populated by the
/// per-provider dependency-injection registrations.
/// </summary>
public interface IGenerationClientRegistry
{
    /// <summary>Registers the client for a provider endpoint.</summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="client">The generation client for that endpoint.</param>
    void Register(string provider, string endpointId, IGenerationClient client);

    /// <summary>Finds the client for a provider endpoint, or null when absent.</summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <returns>The generation client, or null.</returns>
    IGenerationClient? Find(string provider, string endpointId);

    /// <summary>
    /// The registered endpoints as a point-in-time snapshot, for capability
    /// filtering and routing. Order is deterministic (by provider then endpoint
    /// id) so routing outcomes are reproducible.
    /// </summary>
    IReadOnlyList<GenerationEndpoint> Endpoints { get; }
}