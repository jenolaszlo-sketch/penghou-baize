namespace Penghou.Baize.Router;

/// <summary>The ordered endpoints and explanation returned by a route provider.</summary>
/// <param name="Endpoints">Compatible endpoints in attempt order.</param>
/// <param name="Explanation">The structured route decision.</param>
public sealed record LlmRouteResolution(
    IReadOnlyList<ResolvedEndpoint> Endpoints,
    LlmRouteExplanation Explanation);
