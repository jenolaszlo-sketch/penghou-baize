namespace Penghou.Baize.Generation;

/// <summary>
/// Selects a single registered endpoint for a generation request. The executor
/// filters candidates to those whose capabilities satisfy the request before
/// consulting the policy, so a policy implementation ranks rather than re-checks
/// capability support. Policies are replaceable and must stay deterministic.
/// </summary>
public interface IGenerationRoutingPolicy
{
    /// <summary>
    /// Selects the endpoint for <paramref name="request"/> from
    /// <paramref name="candidates"/>, or null when none should be used.
    /// </summary>
    /// <param name="request">The generation request to route.</param>
    /// <param name="candidates">Endpoints whose capabilities satisfy the request, in registry order.</param>
    /// <returns>The selected endpoint, or null when no candidate is acceptable.</returns>
    GenerationEndpoint? Select(
        GenerationRequest request,
        IReadOnlyList<GenerationEndpoint> candidates);
}
