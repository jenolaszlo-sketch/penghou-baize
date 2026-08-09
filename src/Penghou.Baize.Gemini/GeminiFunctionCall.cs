using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Wire model for a function call requested by the Gemini model.
/// </summary>
public sealed class GeminiFunctionCall
{
    /// <summary>The call id, when supplied by the provider.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The function name being called.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The function arguments as a JSON value.</summary>
    [JsonPropertyName("args")]
    public required JsonElement Args { get; init; }
}