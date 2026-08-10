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

/// <summary>A non-text input with a MIME type and provider-neutral source.</summary>
public abstract record LlmMediaContent : LlmContentPart
{
    /// <summary>Initializes media content.</summary>
    protected LlmMediaContent(string mediaType, LlmMediaSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(source);
        MediaType = mediaType;
        Source = source;
    }

    /// <summary>The IANA media type, such as <c>image/png</c>.</summary>
    public string MediaType { get; }

    /// <summary>The media source and transport.</summary>
    public LlmMediaSource Source { get; }
}

/// <summary>An image input.</summary>
public sealed record LlmImageContent : LlmMediaContent
{
    /// <summary>Initializes image content.</summary>
    public LlmImageContent(string mediaType, LlmMediaSource source)
        : base(mediaType, source) { }
}

/// <summary>An audio input.</summary>
public sealed record LlmAudioContent : LlmMediaContent
{
    /// <summary>Initializes audio content.</summary>
    public LlmAudioContent(string mediaType, LlmMediaSource source)
        : base(mediaType, source) { }
}

/// <summary>A video input.</summary>
public sealed record LlmVideoContent : LlmMediaContent
{
    /// <summary>Initializes video content.</summary>
    public LlmVideoContent(string mediaType, LlmMediaSource source)
        : base(mediaType, source) { }
}

/// <summary>A generic document or file input.</summary>
public sealed record LlmFileContent : LlmMediaContent
{
    /// <summary>Initializes file content.</summary>
    public LlmFileContent(
        string mediaType,
        LlmMediaSource source,
        string? fileName = null)
        : base(mediaType, source)
    {
        FileName = fileName;
    }

    /// <summary>The original file name, when known.</summary>
    public string? FileName { get; }
}
