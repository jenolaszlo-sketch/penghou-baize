using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for OpenAI token usage.
/// </summary>
public sealed class OpenAiUsage
{
    /// <summary>Tokens consumed by the input prompt.</summary>
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; init; }

    /// <summary>Tokens generated as output.</summary>
    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; init; }

    /// <summary>Total tokens across prompt and completion.</summary>
    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; init; }

    /// <summary>Prompt tokens served from cache.</summary>
    [JsonPropertyName("prompt_cache_hit_tokens")]
    public int? PromptCacheHitTokens { get; init; }

    /// <summary>Prompt tokens that missed the cache.</summary>
    [JsonPropertyName("prompt_cache_miss_tokens")]
    public int? PromptCacheMissTokens { get; init; }
}
