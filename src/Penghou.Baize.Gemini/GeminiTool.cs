using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Wire model for the <c>tools</c> entry declaring function declarations to
/// the Gemini API.
/// </summary>
public sealed class GeminiTool
{
    /// <summary>The function declarations available to the model.</summary>
    [JsonPropertyName("functionDeclarations")]
    public required List<GeminiFunctionDeclaration> FunctionDeclarations { get; init; }
}

/// <summary>
/// Wire model for a single Gemini function declaration.
/// </summary>
public sealed class GeminiFunctionDeclaration
{
    /// <summary>The function name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A description of what the function does.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The JSON Schema describing the function's parameters.</summary>
    [JsonPropertyName("parameters")]
    public required JsonElement Parameters { get; init; }
}