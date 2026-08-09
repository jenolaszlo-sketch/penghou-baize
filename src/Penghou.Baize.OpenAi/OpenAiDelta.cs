using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for the incremental <c>delta</c> of an OpenAI streaming chunk.
/// </summary>
public sealed class OpenAiDelta
{
    /// <summary>The role of the assistant message.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>Incremental text content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Incremental reasoning content (provider-specific).</summary>
    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; init; }

    /// <summary>Incremental native tool-call deltas.</summary>
    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCallDelta>? ToolCalls { get; init; }
}
