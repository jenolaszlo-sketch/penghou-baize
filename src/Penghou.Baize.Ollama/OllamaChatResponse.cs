using System.Text.Json.Serialization;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Wire model for an Ollama <c>/api/chat</c> response chunk (one per line of
/// the stream).
/// </summary>
public sealed class OllamaChatResponse
{
    /// <summary>The model that produced the response.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>The assistant message for this chunk.</summary>
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    /// <summary>Whether this is the final chunk of the response.</summary>
    [JsonPropertyName("done")]
    public bool Done { get; init; }

    /// <summary>The reason generation finished, for example <c>stop</c> or <c>length</c>.</summary>
    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }

    /// <summary>Total time spent processing the request, in nanoseconds.</summary>
    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; init; }

    /// <summary>Time spent loading the model, in nanoseconds.</summary>
    [JsonPropertyName("load_duration")]
    public long? LoadDuration { get; init; }

    /// <summary>Tokens consumed by the input prompt.</summary>
    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; init; }

    /// <summary>Time spent evaluating the prompt, in nanoseconds.</summary>
    [JsonPropertyName("prompt_eval_duration")]
    public long? PromptEvalDuration { get; init; }

    /// <summary>Tokens generated as output.</summary>
    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; init; }

    /// <summary>Time spent generating output, in nanoseconds.</summary>
    [JsonPropertyName("eval_duration")]
    public long? EvalDuration { get; init; }
}
