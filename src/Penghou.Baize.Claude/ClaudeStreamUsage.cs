using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for usage reported on the Claude <c>message_delta</c> event.
/// It carries only output tokens mid-stream; full usage including input
/// tokens arrives on <c>message_start</c> and is handled separately.
/// </summary>
internal sealed class ClaudeStreamUsage
{
    /// <summary>Output tokens generated so far.</summary>
    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }
}
