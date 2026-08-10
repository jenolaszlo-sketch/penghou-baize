using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for a tool declared to the Claude Messages API.
/// </summary>
internal sealed class ClaudeTool
{
    /// <summary>The tool name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A description of what the tool does.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The JSON Schema describing the tool's input.</summary>
    [JsonPropertyName("input_schema")]
    public required JsonElement InputSchema { get; init; }
}
