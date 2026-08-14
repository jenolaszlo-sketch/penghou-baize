namespace Penghou.Baize.Generation;

/// <summary>
/// The immutable identity of an accepted generation operation. It pins the
/// provider endpoint that accepted the operation: status retrieval and
/// cancellation MUST use the endpoint stored here and MUST NOT invoke routing
/// again. Values can be persisted and used to reconstruct a client later.
/// </summary>
/// <param name="Provider">The provider name (for example <c>OpenAi</c>).</param>
/// <param name="EndpointId">The configured endpoint identity.</param>
/// <param name="Id">The provider-assigned operation id.</param>
/// <param name="Model">The model the operation was submitted to, when known.</param>
public sealed record GenerationOperationHandle(
    string Provider,
    string EndpointId,
    string Id,
    string? Model = null);