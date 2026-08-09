using System.Text.Json.Serialization;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Wire model for Ollama model options.
/// </summary>
public sealed class OllamaOptions
{
    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>The maximum number of tokens to generate.</summary>
    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; init; }
}
