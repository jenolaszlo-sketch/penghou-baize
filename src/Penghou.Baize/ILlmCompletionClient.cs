namespace Penghou.Baize;

/// <summary>
/// Optional client capability for providers that expose a native
/// non-streaming completion endpoint.
/// </summary>
/// <remarks>
/// Callers should normally use <see cref="LlmStreamingExtensions.CompleteAsync(ILlmClient,LlmRequest,Action{string}?,CancellationToken)"/>.
/// That helper uses this interface when available and otherwise drains the
/// canonical event stream, so implementing it is an optimization rather than
/// a requirement for <see cref="ILlmClient"/> implementations.
/// </remarks>
public interface ILlmCompletionClient
{
    /// <summary>Completes a request through the provider's native response path.</summary>
    Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default);
}
