namespace Penghou.Baize.Router.Configuration;

/// <summary>The root <c>LlmRouting</c> configuration section.</summary>
public sealed class LlmRoutingOptions
{
    /// <summary>
    /// Optional provider modules loaded by assembly name from trusted
    /// configuration. Explicit provider-package registration remains the
    /// recommended default.
    /// </summary>
    public List<LlmProviderModuleOptions> ProviderModules { get; init; } = [];

    /// <summary>The registered models, each with one or more endpoints.</summary>
    public List<LlmModelOptions> Models { get; init; } = [];

    /// <summary>
    /// Named capability profiles. An endpoint references one by name through
    /// <see cref="LlmEndpointOptions.Profile"/> to opt in to capabilities the
    /// provider's conservative defaults do not claim (for example a local
    /// Ollama model that does support native tool calling).
    /// </summary>
    public Dictionary<string, LlmEndpointCapabilitiesOptions> Profiles { get; init; } = [];

    /// <summary>
    /// Per-strategy fallback chains. Each chain lists model registration
    /// names in preference order; the router expands each name to all of its
    /// endpoints.
    /// </summary>
    public Dictionary<ModelStrategy, List<string>> StrategyFallbacks { get; init; } = [];

    /// <summary>
    /// The maximum number of concurrently in-flight LLM streams across the
    /// router; 0 (the default) means unbounded.
    /// </summary>
    public int MaxPendingRequests { get; init; }

    /// <summary>
    /// A per-request bound; a stream exceeding it is cancelled and recorded
    /// as an availability failure. Null (the default) means no bound.
    /// </summary>
    public TimeSpan? RequestTimeout { get; init; }
}
