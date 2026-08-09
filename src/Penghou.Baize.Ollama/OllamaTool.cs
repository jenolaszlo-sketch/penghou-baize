using System.Text.Json.Serialization;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Wire model for a tool declared to the Ollama API.
/// </summary>
public sealed class OllamaTool
{
    /// <summary>The tool type; always <c>function</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    /// <summary>The function definition.</summary>
    [JsonPropertyName("function")]
    public required OllamaFunctionDefinition Function { get; init; }
}
