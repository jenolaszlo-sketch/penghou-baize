using System.Text.Json.Serialization;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Wire model for a Gemini <c>generateContent</c> request body. The model is
/// carried in the URL path and streaming is selected by the
/// <c>:streamGenerateContent?alt=sse</c> method, so neither appears here.
/// </summary>
public sealed class GeminiChatRequest
{
    /// <summary>The conversation contents (user, model and tool turns only).</summary>
    [JsonPropertyName("contents")]
    public required List<GeminiChatMessage> Contents { get; init; }

    /// <summary>
    /// The system instruction, collected from all system-role messages. Gemini
    /// does not accept a <c>system</c> role inside <see cref="Contents"/>;
    /// system prompts belong in this top-level property.
    /// </summary>
    [JsonPropertyName("systemInstruction")]
    public GeminiSystemInstruction? SystemInstruction { get; init; }

    /// <summary>Generation configuration such as temperature and response schema.</summary>
    [JsonPropertyName("generationConfig")]
    public GeminiGenerationConfig? GenerationConfig { get; init; }

    /// <summary>Function declarations made available to the model.</summary>
    [JsonPropertyName("tools")]
    public List<GeminiTool>? Tools { get; init; }
}

/// <summary>
/// Wire model for the Gemini <c>systemInstruction</c> object.
/// </summary>
public sealed class GeminiSystemInstruction
{
    /// <summary>The instruction content parts (text parts carrying the system prompt).</summary>
    [JsonPropertyName("parts")]
    public required List<GeminiContentPart> Parts { get; init; }
}

/// <summary>
/// Wire model for the Gemini <c>generationConfig</c> object.
/// </summary>
public sealed class GeminiGenerationConfig
{
    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>The maximum number of tokens to generate.</summary>
    [JsonPropertyName("maxOutputTokens")]
    public int? MaxOutputTokens { get; init; }

    /// <summary>A JSON Schema the response must conform to.</summary>
    [JsonPropertyName("responseSchema")]
    public object? ResponseSchema { get; init; }

    /// <summary>
    /// The response MIME type. Must be <c>application/json</c> when
    /// <see cref="ResponseSchema"/> is set.
    /// </summary>
    [JsonPropertyName("responseMimeType")]
    public string? ResponseMimeType { get; init; }

    /// <summary>Reasoning/thinking budget configuration.</summary>
    [JsonPropertyName("thinkingConfig")]
    public GeminiThinkingConfig? ThinkingConfig { get; init; }
}

/// <summary>
/// Wire model for the Gemini <c>thinkingConfig</c> object controlling the
/// model's thinking token budget.
/// </summary>
public sealed class GeminiThinkingConfig
{
    /// <summary>The maximum number of thinking tokens.</summary>
    [JsonPropertyName("thinkingBudget")]
    public int? ThinkingBudget { get; init; }
}