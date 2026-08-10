using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for a Claude Messages API request.
/// </summary>
internal sealed class ClaudeMessageRequest
{
    /// <summary>The model identifier.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>The system prompt, joined from all system-role messages.</summary>
    [JsonPropertyName("system")]
    public string? System { get; init; }

    /// <summary>The conversation messages (excluding system-role messages).</summary>
    [JsonPropertyName("messages")]
    public required List<ClaudeMessage> Messages { get; init; }

    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>The maximum number of tokens to generate.</summary>
    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; init; }

    /// <summary>Whether to stream the response.</summary>
    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }

    /// <summary>Tools made available to the model.</summary>
    [JsonPropertyName("tools")]
    public List<ClaudeTool>? Tools { get; init; }

    /// <summary>
    /// Extended-thinking configuration. Adaptive models receive
    /// <c>{"type":"adaptive"}</c>; manual-thinking models receive
    /// <c>{"type":"enabled","budget_tokens":N}</c>.
    /// </summary>
    [JsonPropertyName("thinking")]
    public ClaudeThinking? Thinking { get; init; }

    /// <summary>Reasoning effort configuration (extended thinking).</summary>
    [JsonPropertyName("output_config")]
    public ClaudeOutputConfig? OutputConfig { get; init; }
}

/// <summary>
/// Wire model for the <c>thinking</c> object controlling Claude extended
/// thinking. <see cref="Type"/> is <c>adaptive</c> for adaptive-thinking
/// models or <c>enabled</c> for manual-thinking models, in which case
/// <see cref="BudgetTokens"/> is required.
/// </summary>
internal sealed class ClaudeThinking
{
    /// <summary>The thinking type (<c>adaptive</c> or <c>enabled</c>).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// The thinking token budget; required when <see cref="Type"/> is
    /// <c>enabled</c>. Must be a multiple of 1024.
    /// </summary>
    [JsonPropertyName("budget_tokens")]
    public int? BudgetTokens { get; init; }
}

/// <summary>
/// Wire model for the <c>output_config</c> object controlling Claude extended
/// thinking effort.
/// </summary>
internal sealed class ClaudeOutputConfig
{
    /// <summary>The reasoning effort tier (<c>low</c>, <c>medium</c> or <c>high</c>).</summary>
    [JsonPropertyName("effort")]
    public string? Effort { get; init; }
}
