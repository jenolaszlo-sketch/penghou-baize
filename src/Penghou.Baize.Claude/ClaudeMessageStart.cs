using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for a Claude Messages streaming <c>message_start</c> event,
/// which carries the full input usage.
/// </summary>
public sealed class ClaudeMessageStart
{
    /// <summary>The event type (<c>message_start</c>).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The started message envelope, including input usage.</summary>
    [JsonPropertyName("message")]
    public ClaudeMessageStartMessage? Message { get; init; }
}
