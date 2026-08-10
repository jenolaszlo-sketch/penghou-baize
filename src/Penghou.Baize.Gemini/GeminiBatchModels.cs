using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Wire model for a Gemini Files API file resource (<c>POST /upload/v1beta/files</c>).
/// The <c>name</c> field (for example <c>files/abc123</c>) is the reference used
/// when a batch job declares its input file.
/// </summary>
internal sealed class GeminiFile
{
    /// <summary>The file resource name, for example <c>files/abc123</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The human-readable display name supplied at upload.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>The MIME type of the uploaded content.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>A URI reference to the file content, when supplied.</summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }
}

/// <summary>The response envelope returned after a resumable file upload is finalized.</summary>
internal sealed class GeminiFileUploadResponse
{
    /// <summary>The uploaded file resource.</summary>
    [JsonPropertyName("file")]
    public GeminiFile? File { get; init; }
}

/// <summary>
/// Wire model for the Gemini long-running batch operation returned by
/// <c>POST /v1beta/models/{model}:batchGenerateContent</c> and polled through
/// <c>GET /v1beta/{name=batches/*}</c>. While <see cref="Done"/> is false the
/// <see cref="Metadata"/> reports the job state; once done, either
/// <see cref="Error"/> or <see cref="Result"/> is populated.
/// </summary>
internal sealed class GeminiBatchOperation
{
    /// <summary>The operation resource name, for example <c>batches/123456</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Whether the batch job has finished processing.</summary>
    [JsonPropertyName("done")]
    public bool? Done { get; init; }

    /// <summary>The job state and request statistics.</summary>
    [JsonPropertyName("metadata")]
    public GeminiBatchJobMetadata? Metadata { get; init; }

    /// <summary>The job-level failure, when the whole batch failed.</summary>
    [JsonPropertyName("error")]
    public GeminiBatchJobError? Error { get; init; }

    /// <summary>The batch result, when the job succeeded.</summary>
    [JsonPropertyName("response")]
    public JsonElement? Result { get; init; }
}

/// <summary>
/// Wire model for the Gemini batch operation metadata, carrying the job state
/// machine value and, once known, the per-request statistics.
/// </summary>
internal sealed class GeminiBatchJobMetadata
{
    /// <summary>The job state, for example <c>JOB_STATE_SUCCEEDED</c>.</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>Per-request statistics reported by the job.</summary>
    [JsonPropertyName("batchStats")]
    public GeminiBatchJobStats? BatchStats { get; init; }
}

/// <summary>
/// Wire model for the Gemini batch job request statistics.
/// </summary>
internal sealed class GeminiBatchJobStats
{
    /// <summary>The number of requests in the batch.</summary>
    [JsonPropertyName("requestCount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? RequestCount { get; init; }

    /// <summary>The number of requests that succeeded.</summary>
    [JsonPropertyName("successfulRequestCount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? SuccessfulRequestCount { get; init; }

    /// <summary>The number of requests that failed.</summary>
    [JsonPropertyName("failedRequestCount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? FailedRequestCount { get; init; }
}

/// <summary>
/// Wire model for a job-level Gemini batch error, following the standard Gemini
/// REST error shape.
/// </summary>
internal sealed class GeminiBatchJobError
{
    /// <summary>The HTTP status code, when reported.</summary>
    [JsonPropertyName("code")]
    public int? Code { get; init; }

    /// <summary>The human-readable failure description.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>The canonical status, for example <c>RESOURCE_EXHAUSTED</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Wire model for one line of a Gemini batch JSONL result file (or one entry of
/// the inlined result list). Each line correlates back to the input through its
/// <see cref="Key"/> and carries either a <see cref="Response"/> or an
/// <see cref="Error"/>.
/// </summary>
internal sealed class GeminiBatchResultLine
{
    /// <summary>The input request key this line answers.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>The generated content response, when the request succeeded.</summary>
    [JsonPropertyName("response")]
    public JsonElement? Response { get; init; }

    /// <summary>The failure, when the request failed.</summary>
    [JsonPropertyName("error")]
    public JsonElement? Error { get; init; }
}
