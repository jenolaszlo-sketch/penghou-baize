using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for a native tool call in an OpenAI assistant message.
/// </summary>
public sealed class OpenAiToolCall
{
    /// <summary>The call id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The call type; typically <c>function</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The called function and its arguments.</summary>
    [JsonPropertyName("function")]
    public required OpenAiToolCallFunction Function { get; init; }
}
