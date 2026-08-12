using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for the function definition of an OpenAI tool.
/// </summary>
internal sealed class OpenAiFunctionTool
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

    /// <summary>Whether the provider must enforce the parameter schema.</summary>
    [JsonPropertyName("strict")]
    public bool? Strict { get; init; }
}
