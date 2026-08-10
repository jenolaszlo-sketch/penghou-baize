using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for an error reported by the Claude Messages API.
/// </summary>
internal sealed class ClaudeError
{
    /// <summary>The error type, for example <c>overloaded_error</c> or <c>invalid_request_error</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>A human-readable error message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
