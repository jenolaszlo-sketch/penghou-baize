namespace Penghou.Baize.Generation;

/// <summary>Orders compatible generation endpoints using shared host reliability state.</summary>
public interface IGenerationEndpointOrderer
{
    /// <summary>Orders candidates from most to least preferred.</summary>
    Task<IReadOnlyList<GenerationEndpoint>> OrderAsync(
        IReadOnlyList<GenerationEndpoint> candidates,
        CancellationToken cancellationToken = default);
}
