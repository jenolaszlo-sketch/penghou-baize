using System.Text.Json.Serialization;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Wire model for Gemini token usage metadata.
/// </summary>
internal sealed class GeminiUsage
{
    /// <summary>Tokens consumed by the input prompt.</summary>
    [JsonPropertyName("promptTokenCount")]
    public int? PromptTokenCount { get; init; }

    /// <summary>Tokens generated as candidate output.</summary>
    [JsonPropertyName("candidatesTokenCount")]
    public int? CandidatesTokenCount { get; init; }

    /// <summary>Tokens generated for model thinking.</summary>
    [JsonPropertyName("thoughtsTokenCount")]
    public int? ThoughtsTokenCount { get; init; }

    /// <summary>Total tokens across prompt and candidates.</summary>
    [JsonPropertyName("totalTokenCount")]
    public int? TotalTokenCount { get; init; }

    /// <summary>The Gemini service tier used for the request.</summary>
    [JsonPropertyName("serviceTier")]
    public string? ServiceTier { get; init; }
}
