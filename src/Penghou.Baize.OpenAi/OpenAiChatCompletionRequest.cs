using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for an OpenAI Chat Completions request.
/// </summary>
internal sealed class OpenAiChatCompletionRequest
{
    /// <summary>The model identifier.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>The conversation messages.</summary>
    [JsonPropertyName("messages")]
    public required List<OpenAiChatMessage> Messages { get; init; }

    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>The maximum number of tokens to generate.</summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    /// <summary>Whether to stream the response.</summary>
    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }

    /// <summary>Options controlling streamed usage.</summary>
    [JsonPropertyName("stream_options")]
    public OpenAiStreamOptions? StreamOptions { get; init; }

    /// <summary>Native tools made available to the model.</summary>
    [JsonPropertyName("tools")]
    public List<OpenAiTool>? Tools { get; init; }

    /// <summary>Structured output configuration for JSON-schema responses.</summary>
    [JsonPropertyName("response_format")]
    public object? ResponseFormat { get; init; }

    /// <summary>Reasoning effort tier (<c>low</c>, <c>medium</c> or <c>high</c>).</summary>
    [JsonPropertyName("reasoning_effort")]
    public string? ReasoningEffort { get; init; }

    /// <summary>Explicit thinking toggle (<c>enabled</c> or <c>disabled</c>), for DeepSeek-style endpoints.</summary>
    [JsonPropertyName("thinking")]
    public object? Thinking { get; init; }
}
