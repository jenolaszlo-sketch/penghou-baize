namespace Penghou.Baize;

/// <summary>
/// Declares what a single model endpoint can and cannot do. Clients use this
/// to validate a request before transmission and to shape the wire request;
/// features the endpoint does not support are rejected with a
/// <see cref="LlmRequestValidationException"/> rather than silently dropped.
/// </summary>
public sealed record LlmEndpointCapabilities
{
    /// <summary>
    /// Whether the endpoint accepts native tool definitions and returns native
    /// tool-call deltas.
    /// </summary>
    public bool NativeToolCalling { get; init; }

    /// <summary>
    /// Whether the endpoint can return more than one tool call per response.
    /// </summary>
    public bool ParallelToolCalls { get; init; }

    /// <summary>
    /// Whether the endpoint natively constrains the output shape (for example
    /// a <c>response_format</c>, <c>response_schema</c> or <c>format</c>
    /// parameter).
    /// </summary>
    public bool NativeStructuredOutput { get; init; }

    /// <summary>
    /// Whether the endpoint emulates structured output through a synthetic
    /// tool rather than a native parameter (for example Claude).
    /// </summary>
    public bool StructuredOutputViaTool { get; init; }

    /// <summary>Whether the endpoint can turn extended thinking on.</summary>
    public bool Thinking { get; init; }

    /// <summary>Whether the endpoint can explicitly turn extended thinking off.</summary>
    public bool ThinkingDisable { get; init; }

    /// <summary>
    /// Whether the endpoint streams tool-call arguments incrementally, or only
    /// reports them once complete.
    /// </summary>
    public bool StreamingToolCallArguments { get; init; }

    /// <summary>
    /// The reasoning effort levels the endpoint accepts when extended thinking
    /// is enabled. An explicit effort outside this set is rejected with a
    /// <see cref="LlmRequestValidationException"/> rather than silently capped;
    /// <see cref="LlmThinkingEffort.None"/> (no preference) is always accepted.
    /// An empty set (the conservative default) claims no specific effort tiers.
    /// </summary>
    public IReadOnlySet<LlmThinkingEffort> SupportedThinkingEfforts { get; init; } =
        new HashSet<LlmThinkingEffort>();

    /// <summary>
    /// An explicit thinking token budget applied when extended thinking is
    /// enabled. When set, providers that express thinking as a token budget
    /// (for example Gemini) use this value instead of deriving one from the
    /// requested effort, so the caller can match the model's documented range
    /// (for example 32768 for Gemini 2.5 Pro). Null lets the client derive a
    /// budget from the effort.
    /// </summary>
    public int? ThinkingBudget { get; init; }

    /// <summary>The message content types the endpoint accepts.</summary>
    public IReadOnlySet<LlmContentType> ContentTypes { get; init; } =
        new HashSet<LlmContentType> { LlmContentType.Text };
}
