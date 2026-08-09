using Penghou.Baize;

namespace Penghou.Baize.Tools;

/// <summary>
/// Normalizes a raw <see cref="LlmResponse"/> against the set of declared
/// tools: canonicalizes native tool-call arguments against their input
/// schemas and, when no native calls exist, recovers tool calls embedded in
/// plain-text model content.
/// </summary>
public interface ILlmResponseNormalizer
{
    /// <summary>
    /// Normalizes the given response for the provided tools.
    /// </summary>
    /// <param name="response">The raw response to normalize.</param>
    /// <param name="tools">The tools the model was invoked with.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// The normalized response. When no tools are declared, or the response
    /// already carries usable native tool calls, the response is returned
    /// (nearly) unchanged; otherwise tool calls are recovered from
    /// <paramref name="response"/> content.
    /// </returns>
    Task<LlmResponse> NormalizeAsync(
        LlmResponse response,
        IReadOnlyCollection<LlmTool> tools,
        CancellationToken cancellationToken = default);
}
