using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Wire model for the function definition of an Ollama tool.
/// </summary>
public sealed class OllamaFunctionDefinition
{
    /// <summary>The function name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A description of what the function does.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>The JSON Schema describing the function's parameters.</summary>
    [JsonPropertyName("parameters")]
    public required JsonElement Parameters { get; init; }
}
