using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for a non-streaming Claude Messages response.
/// </summary>
internal sealed class ClaudeMessageResponse
{
    /// <summary>The content blocks produced by the model.</summary>
    [JsonPropertyName("content")]
    public List<ClaudeContentBlock>? Content { get; init; }

    /// <summary>The stop reason, for example <c>end_turn</c> or <c>tool_use</c>.</summary>
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    /// <summary>Token usage for the response.</summary>
    [JsonPropertyName("usage")]
    public ClaudeUsage? Usage { get; init; }
}
