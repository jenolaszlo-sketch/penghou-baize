namespace Penghou.Baize.Router;

/// <summary>Immutable input supplied to an <see cref="ILlmRouteProvider"/>.</summary>
/// <param name="Target">The model, strategy, or named route to resolve.</param>
/// <param name="Request">
/// The canonical request used for capability filtering, or null when only a
/// preferred endpoint is being inspected.
/// </param>
public sealed record LlmRoutingContext(
    LlmRouteTarget Target,
    LlmRequest? Request = null);
