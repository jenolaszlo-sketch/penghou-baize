namespace Penghou.Baize.Router;

/// <summary>A safe, structured explanation of one route-resolution decision.</summary>
/// <param name="Target">The requested route target.</param>
/// <param name="ConfiguredModels">The configured model chain considered.</param>
/// <param name="Candidates">Every expanded endpoint and its outcome.</param>
/// <param name="SelectedEndpoint">The first endpoint after filtering and ranking.</param>
public sealed record LlmRouteExplanation(
    LlmRouteTarget Target,
    IReadOnlyList<string> ConfiguredModels,
    IReadOnlyList<LlmRouteCandidateExplanation> Candidates,
    ResolvedEndpoint? SelectedEndpoint)
{
    /// <summary>Whether at least one endpoint was selected.</summary>
    public bool Succeeded => SelectedEndpoint is not null;
}

/// <summary>Explains how one concrete endpoint participated in routing.</summary>
/// <param name="Endpoint">The concrete endpoint.</param>
/// <param name="Compatible">Whether it satisfies the request requirements.</param>
/// <param name="RejectionReason">Why it was filtered, when incompatible.</param>
/// <param name="Rank">Its zero-based rank among compatible endpoints.</param>
/// <param name="Stats">The routing-memory snapshot used for the decision.</param>
public sealed record LlmRouteCandidateExplanation(
    ResolvedEndpoint Endpoint,
    bool Compatible,
    string? RejectionReason,
    int? Rank,
    LlmEndpointStats Stats);
