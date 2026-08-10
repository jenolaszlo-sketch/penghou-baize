using System.Text.Json.Serialization;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Wire model for a single Gemini conversation message.
/// </summary>
internal sealed class GeminiChatMessage
{
    /// <summary>The message role (for example <c>user</c> or <c>model</c>).</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>The content parts making up the message.</summary>
    [JsonPropertyName("parts")]
    public required List<GeminiContentPart> Parts { get; init; }
}

/// <summary>
/// Wire model for a single Gemini content part (text, thought or function call).
/// </summary>
internal sealed class GeminiContentPart
{
    /// <summary>Plain text content.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>When true, the text is model reasoning rather than output.</summary>
    [JsonPropertyName("thought")]
    public bool? Thought { get; init; }

    /// <summary>
    /// Gemini's thought signature for a <c>thought</c> part. When extended
    /// thinking produced the part, Gemini requires this exact signature to be
    /// returned during function-calling conversations, otherwise it can reject
    /// the request with an HTTP 400.
    /// </summary>
    [JsonPropertyName("thoughtSignature")]
    public string? ThoughtSignature { get; init; }

    /// <summary>A function call requested by the model.</summary>
    [JsonPropertyName("functionCall")]
    public GeminiFunctionCall? FunctionCall { get; init; }

    /// <summary>The application's response to a function call.</summary>
    [JsonPropertyName("functionResponse")]
    public GeminiFunctionResponse? FunctionResponse { get; init; }

    /// <summary>Media bytes embedded directly in the request.</summary>
    [JsonPropertyName("inlineData")]
    public GeminiInlineData? InlineData { get; init; }

    /// <summary>A URI understood by Gemini, commonly from the Files API.</summary>
    [JsonPropertyName("fileData")]
    public GeminiFileData? FileData { get; init; }
}

/// <summary>Gemini inline media data.</summary>
internal sealed class GeminiInlineData
{
    /// <summary>The MIME type.</summary>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    /// <summary>The base64-encoded bytes.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

/// <summary>Gemini file or media URI data.</summary>
internal sealed class GeminiFileData
{
    /// <summary>The MIME type.</summary>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    /// <summary>The Gemini file or media URI.</summary>
    [JsonPropertyName("fileUri")]
    public required string FileUri { get; init; }
}
