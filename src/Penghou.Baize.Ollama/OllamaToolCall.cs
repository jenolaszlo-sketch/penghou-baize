using System.Text.Json.Serialization;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Wire model for a native tool call in an Ollama assistant message.
/// </summary>
internal sealed class OllamaToolCall
{
    /// <summary>The call type; typically <c>function</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The called function and its arguments.</summary>
    [JsonPropertyName("function")]
    public required OllamaCalledFunction Function { get; init; }
}
