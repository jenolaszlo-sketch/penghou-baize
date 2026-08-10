using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for OpenAI streaming options.
/// </summary>
internal sealed class OpenAiStreamOptions
{
    /// <summary>Whether to include cumulative usage in the final streamed chunk.</summary>
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage { get; init; }
}
