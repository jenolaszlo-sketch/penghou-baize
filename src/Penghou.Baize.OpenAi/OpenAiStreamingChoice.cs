using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for a streaming OpenAI Chat Completions choice.
/// </summary>
public sealed class OpenAiStreamingChoice
{
    /// <summary>The incremental delta for this chunk.</summary>
    [JsonPropertyName("delta")]
    public OpenAiDelta? Delta { get; init; }

    /// <summary>The finish reason, present on the final chunk.</summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}
