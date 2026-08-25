using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for one OpenAI Chat Completions streaming chunk.
/// </summary>
internal sealed class OpenAiChatCompletionChunk
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

    /// <summary>The per-chunk streaming choices.</summary>
    [JsonPropertyName("choices")]
    public List<OpenAiStreamingChoice>? Choices { get; init; }

    /// <summary>Cumulative usage, present on the final chunk when requested.</summary>
    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}