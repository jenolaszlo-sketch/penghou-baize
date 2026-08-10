using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for a Claude Messages streaming <c>content_block_delta</c>
/// event's <c>delta</c> object.
/// </summary>
internal sealed class ClaudeStreamDelta
{
    /// <summary>The delta type (for example <c>text_delta</c>, <c>thinking_delta</c>, <c>input_json_delta</c>).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Text emitted for <c>text_delta</c> deltas.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Reasoning/thinking text emitted for <c>thinking_delta</c> deltas.</summary>
    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; }

    /// <summary>
    /// The thinking-block signature emitted for <c>signature_delta</c> deltas.
    /// Anthropic requires this exact signature to be replayed with the thinking
    /// text when thinking and tool use are combined.
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    /// <summary>Partial JSON fragment emitted for <c>input_json_delta</c> deltas.</summary>
    [JsonPropertyName("partial_json")]
    public string? PartialJson { get; init; }

    /// <summary>The stop reason emitted on the final <c>message_delta</c>.</summary>
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }
}
