using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for a non-streaming OpenAI Chat Completions choice.
/// </summary>
internal sealed class OpenAiChatChoice
{
    /// <summary>The assistant message.</summary>
    [JsonPropertyName("message")]
    public OpenAiChatMessage? Message { get; init; }

    /// <summary>The finish reason, for example <c>stop</c> or <c>tool_calls</c>.</summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}
