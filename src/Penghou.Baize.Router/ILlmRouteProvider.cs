namespace Penghou.Baize.Router;

/// <summary>
/// Resolves an application routing target into an ordered endpoint attempt
/// list. Implement this interface directly to replace Baize route resolution.
/// </summary>
public interface ILlmRouteProvider
{
    /// <summary>Resolves and explains an endpoint candidate chain.</summary>
    ValueTask<LlmRouteResolution> ResolveAsync(
        LlmRoutingContext context,
        CancellationToken cancellationToken = default);
}
