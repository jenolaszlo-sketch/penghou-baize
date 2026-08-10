using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for one OpenAI Chat Completions streaming chunk.
/// </summary>
internal sealed class OpenAiChatCompletionChunk
{
    /// <summary>The per-chunk streaming choices.</summary>
    [JsonPropertyName("choices")]
    public List<OpenAiStreamingChoice>? Choices { get; init; }

    /// <summary>Cumulative usage, present on the final chunk when requested.</summary>
    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}