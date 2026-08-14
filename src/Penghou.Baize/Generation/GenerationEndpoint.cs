namespace Penghou.Baize.Generation;

/// <summary>
/// A registered generation endpoint and its client, used by the executor to
/// filter and rank candidates for a request before submission.
/// </summary>
/// <param name="Provider">The provider name (for example <c>OpenAi</c>).</param>
/// <param name="EndpointId">The configured endpoint identity.</param>
/// <param name="Client">The generation client for that endpoint.</param>
public sealed record GenerationEndpoint(
    string Provider,
    string EndpointId,
    IGenerationClient Client);
