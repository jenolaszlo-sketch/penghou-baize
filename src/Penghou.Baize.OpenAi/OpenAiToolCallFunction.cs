using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for the function of a complete native OpenAI tool call.
/// </summary>
public sealed class OpenAiToolCallFunction
{
    /// <summary>The function name being called.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The function arguments as a JSON string.</summary>
    [JsonPropertyName("arguments")]
    public required string Arguments { get; init; }
}
