using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for a non-streaming OpenAI Chat Completions response.
/// </summary>
internal sealed class OpenAiChatCompletionResponse
{
    /// <summary>Provider-assigned completion identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The model that actually served the request.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>The serving fingerprint, when reported.</summary>
    [JsonPropertyName("system_fingerprint")]
    public string? SystemFingerprint { get; init; }

    /// <summary>The service tier used for the request, when reported.</summary>
    [JsonPropertyName("service_tier")]
    public string? ServiceTier { get; init; }

    /// <summary>The generated choices.</summary>
    [JsonPropertyName("choices")]
    public List<OpenAiChatChoice>? Choices { get; init; }

    /// <summary>Token usage for the response.</summary>
    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}