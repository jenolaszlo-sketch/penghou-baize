using System.Text.Json.Serialization;

namespace Penghou.Baize.Runway;

/// <summary>
/// The response returned by a Runway generation endpoint when a task is created.
/// </summary>
/// <param name="Id">The provider-assigned task id, used for status retrieval and cancellation.</param>
/// <param name="EstimatedCost">The maximum credits this task may charge.</param>
public sealed record RunwayTaskCreateResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("estimatedCost")] RunwayEstimatedCost? EstimatedCost);

/// <summary>The estimated cost of a task in credits.</summary>
/// <param name="Credits">The estimated or maximum credits for the task.</param>
public sealed record RunwayEstimatedCost(
    [property: JsonPropertyName("credits")] double? Credits);

/// <summary>The final cost of a terminal task in credits.</summary>
/// <param name="Credits">The credits charged for the task; a refunded task reports 0.</param>
public sealed record RunwayCost(
    [property: JsonPropertyName("credits")] double? Credits);

/// <summary>
/// A Runway task snapshot. Runway generation is asynchronous: a creation call
/// returns a task id, and the client polls <c>GET /v1/tasks/{id}</c> until the
/// task reaches <c>SUCCEEDED</c> or <c>FAILED</c>. Output URLs expire within
/// 24–48 hours; fetch the task again to get fresh URLs.
/// </summary>
/// <param name="Id">The provider-assigned task id.</param>
/// <param name="Status">The provider task status.</param>
/// <param name="CreatedAt">The timestamp the task was submitted at.</param>
/// <param name="Progress">Completion fraction between 0 and 1 while <c>RUNNING</c>.</param>
/// <param name="Output">Output asset URLs when the task succeeded.</param>
/// <param name="Failure">A human-friendly failure reason when the task failed.</param>
/// <param name="FailureCode">A machine-readable failure code when the task failed.</param>
/// <param name="EstimatedCost">Estimated cost, computed against current pricing.</param>
/// <param name="Cost">Final cost for a terminal task.</param>
public sealed record RunwayTask(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("createdAt")] string? CreatedAt,
    [property: JsonPropertyName("progress")] double? Progress,
    [property: JsonPropertyName("output")] IReadOnlyList<string>? Output,
    [property: JsonPropertyName("failure")] string? Failure,
    [property: JsonPropertyName("failureCode")] string? FailureCode,
    [property: JsonPropertyName("estimatedCost")] RunwayEstimatedCost? EstimatedCost,
    [property: JsonPropertyName("cost")] RunwayCost? Cost);

/// <summary>
/// The provider-faithful wire body for <c>POST /v1/text_to_video</c>.
/// </summary>
public sealed record RunwayTextToVideoRequest
{
    /// <summary>The model identifier, for example <c>gen4.5</c>.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>A non-empty prompt describing what should appear in the output.</summary>
    [JsonPropertyName("promptText")]
    public required string PromptText { get; init; }

    /// <summary>The resolution of the output video, for example <c>1280:720</c>.</summary>
    [JsonPropertyName("ratio")]
    public string? Ratio { get; init; }

    /// <summary>The duration of the output video in seconds.</summary>
    [JsonPropertyName("duration")]
    public int? Duration { get; init; }

    /// <summary>A deterministic seed; the same seed for an identical request produces similar results.</summary>
    [JsonPropertyName("seed")]
    public int? Seed { get; init; }

    /// <summary>The output container/encoding: <c>mp4</c>, <c>prores</c>, or <c>png_sequence</c>.</summary>
    [JsonPropertyName("outputFormat")]
    public string? OutputFormat { get; init; }

    /// <summary>Whether to generate audio for the video, for models that support it.</summary>
    [JsonPropertyName("audio")]
    public bool? Audio { get; init; }

    /// <summary>Text describing what should not appear in the output, for models that support it.</summary>
    [JsonPropertyName("negativePrompt")]
    public string? NegativePrompt { get; init; }
}

/// <summary>
/// The request body for <c>POST /v1/uploads</c>, which reserves an ephemeral
/// upload slot for a media file. The response carries a presigned
/// <c>uploadUrl</c> and form <c>fields</c>; after the file is posted there, the
/// returned <c>runwayUri</c> (a <c>runway://</c> URI) can be used as an input
/// image or video reference in generation requests.
/// </summary>
public sealed record RunwayUploadCreateRequest
{
    /// <summary>The file name with a valid media extension (image, video, or audio).</summary>
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    /// <summary>The upload type; Runway currently uses <c>ephemeral</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "ephemeral";
}

/// <summary>
/// The response from <c>POST /v1/uploads</c>. The file bytes must be posted to
/// <see cref="UploadUrl"/> as a multipart form containing <see cref="Fields"/>
/// plus the file part; the resulting <see cref="RunwayUri"/> is then usable as
/// an input media reference.
/// </summary>
public sealed record RunwayUploadCreateResponse
{
    /// <summary>The identifier of the reserved upload slot.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The presigned URL that completes the multipart upload.</summary>
    [JsonPropertyName("uploadUrl")]
    public string? UploadUrl { get; init; }

    /// <summary>
    /// The <c>runway://</c> URI to reference the uploaded file in generation
    /// requests once the multipart upload completes.
    /// </summary>
    [JsonPropertyName("runwayUri")]
    public string? RunwayUri { get; init; }

    /// <summary>The form fields to include alongside the file part in the multipart upload.</summary>
    [JsonPropertyName("fields")]
    public IReadOnlyDictionary<string, string>? Fields { get; init; }
}

/// <summary>
/// The provider-faithful wire body for <c>POST /v1/image_to_video</c>.
/// </summary>
public sealed record RunwayImageToVideoRequest
{
    /// <summary>The model identifier, for example <c>gen4.5</c>.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// The first-frame image as an HTTPS URL, Runway upload URI, or base64 data
    /// URI (for example <c>data:image/png;base64,...</c>).
    /// </summary>
    [JsonPropertyName("promptImage")]
    public required string PromptImage { get; init; }

    /// <summary>A non-empty prompt describing how the video should evolve.</summary>
    [JsonPropertyName("promptText")]
    public required string PromptText { get; init; }

    /// <summary>The resolution of the output video, for example <c>1280:720</c>.</summary>
    [JsonPropertyName("ratio")]
    public string? Ratio { get; init; }

    /// <summary>The duration of the output video in seconds.</summary>
    [JsonPropertyName("duration")]
    public int? Duration { get; init; }

    /// <summary>A deterministic seed; the same seed for an identical request produces similar results.</summary>
    [JsonPropertyName("seed")]
    public int? Seed { get; init; }

    /// <summary>The output container/encoding: <c>mp4</c>, <c>prores</c>, or <c>png_sequence</c>.</summary>
    [JsonPropertyName("outputFormat")]
    public string? OutputFormat { get; init; }

    /// <summary>Whether to generate audio for the video, for models that support it.</summary>
    [JsonPropertyName("audio")]
    public bool? Audio { get; init; }

    /// <summary>Text describing what should not appear in the output, for models that support it.</summary>
    [JsonPropertyName("negativePrompt")]
    public string? NegativePrompt { get; init; }
}