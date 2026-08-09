using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for a tool declared to the OpenAI API.
/// </summary>
public sealed class OpenAiTool
{
    /// <summary>The tool type; always <c>function</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    /// <summary>The function definition.</summary>
    [JsonPropertyName("function")]
    public required OpenAiFunctionTool Function { get; init; }
}
