using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for the incremental function fragments of an OpenAI tool-call delta.
/// </summary>
internal sealed class OpenAiToolCallFunctionDelta
{
    /// <summary>An incremental fragment of the function name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>An incremental fragment of the arguments JSON.</summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}