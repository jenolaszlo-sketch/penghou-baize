using Penghou.Baize;

namespace Penghou.Baize.Router.Configuration;

/// <summary>
/// Per-endpoint capability overrides. Each property is nullable: a null value
/// falls back to the provider's default, an explicit value wins. Mirrors
/// <see cref="LlmEndpointCapabilities"/> with nullable members so configuration
/// can distinguish "unspecified" from "false".
/// </summary>
public sealed class LlmEndpointCapabilitiesOptions
{
    /// <summary>Whether the endpoint accepts native tool definitions.</summary>
    public bool? NativeToolCalling { get; init; }

    /// <summary>Whether the endpoint can return multiple tool calls per response.</summary>
    public bool? ParallelToolCalls { get; init; }

    /// <summary>Whether the endpoint enforces strict tool-argument schemas.</summary>
    public bool? StrictToolArguments { get; init; }

    /// <summary>Whether tools and structured output can be combined.</summary>
    public bool? ToolsWithStructuredOutput { get; init; }

    /// <summary>Whether the endpoint natively constrains the output shape.</summary>
    public bool? NativeStructuredOutput { get; init; }

    /// <summary>Whether the endpoint emulates structured output through a synthetic tool.</summary>
    public bool? StructuredOutputViaTool { get; init; }

    /// <summary>Whether the endpoint can turn extended thinking on.</summary>
    public bool? Thinking { get; init; }

    /// <summary>Whether the endpoint can explicitly turn extended thinking off.</summary>
    public bool? ThinkingDisable { get; init; }

    /// <summary>Whether the endpoint streams tool-call arguments incrementally.</summary>
    public bool? StreamingToolCallArguments { get; init; }

    /// <summary>
    /// The reasoning effort levels the endpoint accepts when extended thinking
    /// is enabled; null keeps the style default.
    /// </summary>
    public IReadOnlyList<LlmThinkingEffort>? SupportedThinkingEfforts { get; init; }

    /// <summary>
    /// An explicit thinking token budget applied when extended thinking is
    /// enabled; null lets the client derive a budget from the effort.
    /// </summary>
    public int? ThinkingBudget { get; init; }

    /// <summary>The message content types the endpoint accepts; null keeps the style default.</summary>
    public IReadOnlyList<LlmContentType>? ContentTypes { get; init; }

    /// <summary>Accepted transports for each non-text content type.</summary>
    public Dictionary<LlmContentType, LlmContentTransport>? ContentTransports
    { get; init; }

    /// <summary>
    /// The asynchronous batch operations the endpoint supports; null keeps the
    /// provider's conservative default. Configure this explicitly for endpoints
    /// that do (or do not) expose a batch API regardless of their API style.
    /// </summary>
    public BatchCapabilities? Batch { get; init; }
}
