using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for a non-streaming OpenAI Chat Completions response.
/// </summary>
public sealed class OpenAiChatCompletionResponse
{
    /// <summary>The generated choices.</summary>
    [JsonPropertyName("choices")]
    public List<OpenAiChatChoice>? Choices { get; init; }

    /// <summary>Token usage for the response.</summary>
    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}
