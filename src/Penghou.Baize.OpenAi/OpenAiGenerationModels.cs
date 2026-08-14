using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

internal sealed class OpenAiImageGenerationRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; set; }

    [JsonPropertyName("n")]
    public int? N { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("aspect_ratio")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("output_format")]
    public string? OutputFormat { get; set; }

    [JsonPropertyName("seed")]
    public int? Seed { get; set; }
}

internal sealed class OpenAiImageGenerationResponse
{
    [JsonPropertyName("created")]
    public long? Created { get; set; }

    [JsonPropertyName("data")]
    public List<OpenAiImageData>? Data { get; set; }
}

internal sealed class OpenAiImageData
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("b64_json")]
    public string? B64Json { get; set; }

    [JsonPropertyName("revised_prompt")]
    public string? RevisedPrompt { get; set; }
}

internal sealed class OpenAiVideoGenerationRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("n")]
    public int? N { get; set; }

    [JsonPropertyName("seed")]
    public int? Seed { get; set; }
}

internal sealed class OpenAiVideo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("progress")]
    public double? Progress { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("content")]
    public List<OpenAiVideoContent>? Content { get; set; }

    [JsonPropertyName("error")]
    public OpenAiVideoError? Error { get; set; }
}

internal sealed class OpenAiVideoContent
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class OpenAiVideoError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class OpenAiSpeechGenerationRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("input")]
    public required string Input { get; set; }

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; set; }
}