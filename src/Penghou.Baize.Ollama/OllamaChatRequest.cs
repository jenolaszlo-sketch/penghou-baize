using System.Text.Json.Serialization;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Wire model for an Ollama <c>/api/chat</c> request.
/// </summary>
internal sealed class OllamaChatRequest
{
    /// <summary>The model identifier.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>The conversation messages.</summary>
    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaMessage> Messages { get; init; }

    /// <summary>Whether to stream the response.</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    /// <summary>Native tools made available to the model.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<OllamaTool>? Tools { get; init; }

    /// <summary>Model options such as temperature and token limit.</summary>
    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; init; }

    /// <summary>A JSON Schema the response must conform to.</summary>
    [JsonPropertyName("format")]
    public object? Format { get; init; }
}
