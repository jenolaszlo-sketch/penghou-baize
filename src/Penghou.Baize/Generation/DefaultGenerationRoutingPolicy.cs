namespace Penghou.Baize.Generation;

/// <summary>
/// The default deterministic routing policy: selects the first candidate that
/// satisfies the request, preserving registry order. There is no automatic
/// fallback between providers because the executor routes before acceptance;
/// re-ranking on provider failure is deliberately left to a custom policy.
/// </summary>
public sealed class DefaultGenerationRoutingPolicy : IGenerationRoutingPolicy
{
    /// <inheritdoc />
    public GenerationEndpoint? Select(
        GenerationRequest request,
        IReadOnlyList<GenerationEndpoint> candidates) =>
        candidates.Count > 0 ? candidates[0] : null;
}
