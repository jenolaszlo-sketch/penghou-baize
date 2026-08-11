namespace Penghou.Baize.Router;

/// <summary>Identifies how a routing request addresses its candidate chain.</summary>
public enum LlmRouteKind
{
    /// <summary>A single logical model registration.</summary>
    Model,

    /// <summary>A built-in typed strategy fallback chain.</summary>
    Strategy,

    /// <summary>An application-defined named fallback chain.</summary>
    Named
}
