using System.Text.Json.Serialization;

namespace Penghou.Baize.Gemini;

/// <summary>Request body for <c>POST /v1beta/interactions</c> image generation.</summary>
internal sealed class GeminiInteractionsRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("input")]
    public List<GeminiInteractionPart>? Input { get; set; }

    [JsonPropertyName("store")]
    public bool? Store { get; set; }

    [JsonPropertyName("response_format")]
    public GeminiImageResponseFormat? ResponseFormat { get; set; }
}

/// <summary>A single input or output part of an Interactions request/response.</summary>
internal sealed class GeminiInteractionPart
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

/// <summary>Image output constraints for an Interactions request.</summary>
internal sealed class GeminiImageResponseFormat
{
    [JsonPropertyName("type")]
    public string? Type { get; set; } = "image";

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("aspect_ratio")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("image_size")]
    public string? ImageSize { get; set; }
}

/// <summary>A step of an Interactions response.</summary>
internal sealed class GeminiInteractionStep
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("content")]
    public List<GeminiInteractionPart>? Content { get; set; }
}

/// <summary>Response body for <c>POST /v1beta/interactions</c>.</summary>
internal sealed class GeminiInteractionsResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("steps")]
    public List<GeminiInteractionStep>? Steps { get; set; }

    [JsonPropertyName("output_image")]
    public GeminiInteractionPart? OutputImage { get; set; }

    [JsonPropertyName("error")]
    public GeminiInteractionsError? Error { get; set; }
}

/// <summary>A structured error carried by an Interactions response.</summary>
internal sealed class GeminiInteractionsError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
