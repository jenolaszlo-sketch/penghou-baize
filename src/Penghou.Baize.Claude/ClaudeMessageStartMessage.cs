using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for the <c>message</c> object of a Claude Messages streaming
/// <c>message_start</c> event.
/// </summary>
public sealed class ClaudeMessageStartMessage
{
    /// <summary>Full input usage reported at the start of a streaming response.</summary>
    [JsonPropertyName("usage")]
    public ClaudeUsage? Usage { get; init; }
}
