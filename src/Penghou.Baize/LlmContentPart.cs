namespace Penghou.Baize;

/// <summary>
/// A single block of content within a <see cref="LlmMessage"/>. A message can
/// carry several parts so that a conversation faithfully preserves assistant
/// tool calls, tool results, reasoning text, and plain text in any order.
/// </summary>
public abstract record LlmContentPart
{
    /// <summary>
    /// Provider-specific continuation metadata (for example Gemini's thought
    /// signature) required to replay this exact part on a later turn, when
    /// the provider supplies it. Attached to the part rather than the message
    /// because such signatures are positional.
    /// </summary>
    public LlmProviderContinuation? Continuation { get; init; }
}

/// <summary>A plain-text content block.</summary>
/// <param name="Text">The text.</param>
public sealed record LlmTextContent(string Text) : LlmContentPart;

/// <summary>
/// A model-reasoning content block. Kept so reasoning produced by a previous
/// turn (for example DeepSeek's <c>reasoning_content</c>) can be returned to
/// the model on later turns. Providers that cannot accept reasoning back
/// simply drop these parts.
/// </summary>
/// <param name="Text">The reasoning text.</param>
public sealed record LlmReasoningContent(string Text) : LlmContentPart;

/// <summary>
/// A tool call made by an assistant message. Each tool call is its own part,
/// matching the block model used by Anthropic and the tool-call arrays used
/// by OpenAI and Ollama.
/// </summary>
/// <param name="ToolCall">The tool call the model produced.</param>
public sealed record LlmToolCallContent(LlmToolCall ToolCall) : LlmContentPart;

/// <summary>
/// The result of executing a tool call, feeding it back to the model for the
/// next turn. A message carrying these parts conventionally uses the
/// <c>tool</c> role.
/// </summary>
/// <param name="Result">The executed tool call's result.</param>
public sealed record LlmToolResultContent(LlmToolResult Result) : LlmContentPart;
