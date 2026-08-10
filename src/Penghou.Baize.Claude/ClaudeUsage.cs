using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for Claude Messages token usage.
/// </summary>
internal sealed class ClaudeUsage
{
    /// <summary>Tokens consumed by the input.</summary>
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    /// <summary>Tokens generated as output.</summary>
    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }

    /// <summary>Input tokens served from prompt cache.</summary>
    [JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; init; }

    /// <summary>Input tokens written to the prompt cache.</summary>
    [JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; init; }
}
