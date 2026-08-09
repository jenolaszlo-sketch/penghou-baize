using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for a single OpenAI chat message.
/// </summary>
public sealed class OpenAiChatMessage
{
    /// <summary>The message role (for example <c>system</c>, <c>user</c> or <c>assistant</c>).</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>The message text content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

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
