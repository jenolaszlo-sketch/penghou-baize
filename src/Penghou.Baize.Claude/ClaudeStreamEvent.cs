using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for a Claude Messages streaming event (SSE data payload).
/// </summary>
public sealed class ClaudeStreamEvent
{
    /// <summary>The event type, for example <c>message_start</c>, <c>content_block_start</c>, <c>content_block_delta</c> or <c>message_delta</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The zero-based index of the content block an event refers to.</summary>
    [JsonPropertyName("index")]
    public int? Index { get; init; }

    /// <summary>The content block for <c>content_block_start</c> events.</summary>
    [JsonPropertyName("content_block")]
    public ClaudeContentBlock? ContentBlock { get; init; }

    /// <summary>The delta payload for <c>content_block_delta</c> and <c>message_delta</c> events.</summary>
    [JsonPropertyName("delta")]
    public ClaudeStreamDelta? Delta { get; init; }

    /// <summary>Mid-stream usage for <c>message_delta</c> events.</summary>
    [JsonPropertyName("usage")]
    public ClaudeStreamUsage? Usage { get; init; }

    /// <summary>Error details for <c>error</c> events.</summary>
    [JsonPropertyName("error")]
    public ClaudeError? Error { get; init; }
}
