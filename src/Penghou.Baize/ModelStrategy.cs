namespace Penghou.Baize;

/// <summary>
/// Identifies the capability a routed call is targeting. The router uses the
/// strategy to select a named fallback chain (see
/// <c>LlmRoutingOptions.StrategyFallbacks</c>) and the prompt builder to shape
/// the request. A strategy is a routing key, not a capability negotiation: it
/// is the configuration's responsibility to point each strategy at models
/// whose endpoints can handle the request. When a candidate endpoint declares
/// capabilities that cannot express the built request, request validation
/// raises <see cref="LlmRequestValidationException"/> before any output and
/// the router advances to the next candidate rather than aborting the chain.
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
