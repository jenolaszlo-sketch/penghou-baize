namespace Penghou.Baize.Router;

/// <summary>Ranks endpoints after hard request-capability filtering.</summary>
public interface ILlmEndpointSelectionPolicy
{
    /// <summary>Orders compatible endpoint candidates from most to least preferred.</summary>
    Task<IReadOnlyList<ResolvedEndpoint>> OrderAsync(
        IReadOnlyList<ResolvedEndpoint> candidates,
        LlmRequest? request,
        ModelStrategy strategy,
        ILlmRouterMemory memory,
        CancellationToken cancellationToken = default);
}
