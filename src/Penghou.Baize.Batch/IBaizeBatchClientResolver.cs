using Penghou.Baize.Router;

namespace Penghou.Baize.Batch;

/// <summary>
/// Resolves a provider batch client for a configured endpoint id, mirroring how
/// <see cref="ILlmModelLookup"/> resolves chat clients. Used to reconnect to an
/// existing provider batch purely from a serialized
/// <see cref="ProviderBatchHandle.EndpointId"/>.
/// </summary>
public interface IBaizeBatchClientResolver
{
    /// <summary>Returns the batch client for a configured endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <returns>The matching batch client.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the endpoint has no registered batch client.</exception>
    IBaizeBatchClient GetClient(string endpointId);

    /// <summary>Tries to return the batch client for a configured endpoint.</summary>
    /// <param name="endpointId">The endpoint's stable id.</param>
    /// <param name="client">The matching batch client when found.</param>
    /// <returns><c>true</c> when the endpoint has a registered batch client; otherwise <c>false</c>.</returns>
    bool TryGetClient(string endpointId, out IBaizeBatchClient client);
}
