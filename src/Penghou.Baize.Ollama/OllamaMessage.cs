using System.Text.Json.Serialization;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Wire model for a single Ollama chat message.
/// </summary>
internal sealed class OllamaMessage
{
    /// <summary>The message role (for example <c>user</c> or <c>assistant</c>).</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>The message text content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Model reasoning emitted separately from user-visible content.</summary>
    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; }

    /// <summary>Native tool calls produced by the model.</summary>
    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<OllamaToolCall>? ToolCalls { get; init; }

    /// <summary>Base64-encoded image inputs for multimodal models.</summary>
    [JsonPropertyName("images")]
    public IReadOnlyList<string>? Images { get; init; }
}
