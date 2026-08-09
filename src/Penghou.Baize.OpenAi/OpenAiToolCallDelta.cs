using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for an incremental native tool-call delta in an OpenAI stream.
/// </summary>
public sealed class OpenAiToolCallDelta
{
    /// <summary>The zero-based index identifying which tool call this delta extends.</summary>
    [JsonPropertyName("index")]
    public required int Index { get; init; }

    /// <summary>The call id, emitted with the first delta.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The call type; typically <c>function</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The function-name/arguments fragments for this delta.</summary>
    [JsonPropertyName("function")]
    public OpenAiToolCallFunctionDelta? Function { get; init; }
}
