namespace Penghou.Baize.Router;

/// <summary>Structured categories for failures before an endpoint call starts.</summary>
public enum LlmRoutingFailureKind
{
    /// <summary>The requested logical model is not registered.</summary>
    ModelNotFound,

    /// <summary>The strategy or named route has no configured chain.</summary>
    RouteNotFound,

    /// <summary>The chain exists but none of its model registrations resolve.</summary>
    NoRegisteredEndpoint,

    /// <summary>Every resolved endpoint was rejected by request requirements.</summary>
    NoCompatibleEndpoint,

    /// <summary>A custom route provider returned an inconsistent or unknown endpoint.</summary>
    InvalidProviderResult
}
