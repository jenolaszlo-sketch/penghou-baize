using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Wire model for the function a native Ollama tool call invokes.
/// </summary>
internal sealed class OllamaCalledFunction
{
    /// <summary>The zero-based index of the call within the message.</summary>
    [JsonPropertyName("index")]
    public int? Index { get; init; }

    /// <summary>The function name being called.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The function arguments as a JSON value.</summary>
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; init; }
}
