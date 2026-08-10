using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for a single OpenAI chat message.
/// </summary>
internal sealed class OpenAiChatMessage
{
    /// <summary>The message role (for example <c>system</c>, <c>user</c> or <c>assistant</c>).</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>The message text content.</summary>
    [JsonPropertyName("content")]
    public object? Content { get; init; }

    /// <summary>Native tool calls made by the assistant.</summary>
    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// The identifier of the tool call a <c>tool</c>-role message answers.
    /// </summary>
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Reasoning produced by a prior assistant turn (DeepSeek-compatible), fed
    /// back so the model can continue after a tool call.
    /// </summary>
    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; init; }
}

/// <summary>A typed OpenAI multimodal message content part.</summary>
internal sealed class OpenAiMessageContentPart
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("image_url")]
    public OpenAiImageUrl? ImageUrl { get; init; }

    [JsonPropertyName("input_audio")]
    public OpenAiInputAudio? InputAudio { get; init; }

    [JsonPropertyName("file")]
    public OpenAiInputFile? File { get; init; }
}

/// <summary>An OpenAI image URL, including a data URL.</summary>
internal sealed class OpenAiImageUrl
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }
}

/// <summary>OpenAI inline audio input.</summary>
internal sealed class OpenAiInputAudio
{
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    [JsonPropertyName("format")]
    public required string Format { get; init; }
}

/// <summary>OpenAI file input.</summary>
internal sealed class OpenAiInputFile
{
    [JsonPropertyName("file_id")]
    public string? FileId { get; init; }

    [JsonPropertyName("file_data")]
    public string? FileData { get; init; }

    [JsonPropertyName("filename")]
    public string? FileName { get; init; }
}
