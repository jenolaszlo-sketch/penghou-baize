using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for a single Claude Messages conversation message.
/// </summary>
internal sealed class ClaudeMessage
{
    /// <summary>The message role (for example <c>user</c> or <c>assistant</c>).</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>The message content as a list of content blocks.</summary>
    [JsonPropertyName("content")]
    public List<ClaudeContentBlock>? Content { get; init; }
}
