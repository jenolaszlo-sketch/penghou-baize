using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for a single Claude Messages content block.
/// </summary>
internal sealed class ClaudeContentBlock
{
    /// <summary>The block type, for example <c>text</c>, <c>tool_use</c> or <c>tool_result</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Text content for <c>text</c> blocks.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// The thinking text for <c>thinking</c> blocks. Sent back to the API when
    /// replaying a conversation that combined thinking and tool use.
    /// </summary>
    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; }

    /// <summary>
    /// The signature for <c>thinking</c> blocks. Anthropic requires the exact
    /// signature received from the model to be returned with the thinking text.
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    /// <summary>Opaque encrypted data for <c>redacted_thinking</c> blocks.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>The tool call id for <c>tool_use</c> blocks.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The tool name for <c>tool_use</c> blocks.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The tool arguments for <c>tool_use</c> blocks.</summary>
    [JsonPropertyName("input")]
    public JsonElement? Input { get; init; }

    /// <summary>The id of the tool call a <c>tool_result</c> block answers.</summary>
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; init; }

    /// <summary>The result content for <c>tool_result</c> blocks.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Whether a <c>tool_result</c> block reports an error.</summary>
    [JsonPropertyName("is_error")]
    public bool? IsError { get; init; }

    /// <summary>The source for an image or document block.</summary>
    [JsonPropertyName("source")]
    public ClaudeContentSource? Source { get; init; }
}

/// <summary>An Anthropic image or document source.</summary>
internal sealed class ClaudeContentSource
{
    /// <summary>The source type: base64, URL, or provider file.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The MIME type for base64 content.</summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    /// <summary>The base64-encoded data.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>The externally reachable URL.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>The Anthropic file identifier.</summary>
    [JsonPropertyName("file_id")]
    public string? FileId { get; init; }
}
