namespace Penghou.Baize;

/// <summary>
/// Identifies the capability a routed call is targeting. The router uses the
/// strategy to select endpoints and the prompt builder to shape the request.
/// </summary>
public enum ModelStrategy
{
    /// <summary>No specific capability; the router applies its default selection.</summary>
    Auto,

    /// <summary>The call is expected to produce tool calls.</summary>
    ToolCall,

    /// <summary>The call must produce output matching a JSON schema.</summary>
    StructuredOutput
}
