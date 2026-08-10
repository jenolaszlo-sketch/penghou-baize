using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Wire model for an OpenAI File object as returned by <c>POST /files</c>.
/// </summary>
internal sealed class OpenAiFile
{
    /// <summary>The file identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The file processing status, for example <c>uploaded</c> or <c>processed</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Wire model for an OpenAI Batch object, as returned by <c>POST /batches</c>
/// and <c>GET /batches/{id}</c>.
/// </summary>
internal sealed class OpenAiBatch
{
    /// <summary>The batch identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The batch lifecycle status (for example <c>validating</c> or <c>completed</c>).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>The uploaded input file identifier.</summary>
    [JsonPropertyName("input_file_id")]
    public string? InputFileId { get; init; }

    /// <summary>The output file identifier, populated once results exist.</summary>
    [JsonPropertyName("output_file_id")]
    public string? OutputFileId { get; init; }

    /// <summary>The error file identifier, populated when any request failed.</summary>
    [JsonPropertyName("error_file_id")]
    public string? ErrorFileId { get; init; }

    /// <summary>Per-request completion counts, when reported.</summary>
    [JsonPropertyName("request_counts")]
    public OpenAiBatchRequestCounts? RequestCounts { get; init; }

    /// <summary>Client-supplied metadata echoed back by the API, when any.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>Per-request completion counts for an OpenAI batch.</summary>
internal sealed class OpenAiBatchRequestCounts
{
    /// <summary>Total requests in the batch.</summary>
    [JsonPropertyName("total")]
    public int? Total { get; init; }

    /// <summary>Requests that completed successfully.</summary>
    [JsonPropertyName("completed")]
    public int? Completed { get; init; }

    /// <summary>Requests that failed.</summary>
    [JsonPropertyName("failed")]
    public int? Failed { get; init; }

    /// <summary>Requests that were cancelled.</summary>
    [JsonPropertyName("cancelled")]
    public int? Cancelled { get; init; }
}

/// <summary>
/// Wire model for one JSONL line of an OpenAI batch output file
/// (<c>GET /files/{output_file_id}/content</c>).
/// </summary>
internal sealed class OpenAiBatchOutputLine
{
    /// <summary>The batch request identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The correlation identifier echoed from the submitted item.</summary>
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; init; }

    /// <summary>The HTTP result envelope, when the item produced one.</summary>
    [JsonPropertyName("response")]
    public OpenAiBatchOutputResponse? Response { get; init; }

    /// <summary>A top-level error, when the item failed outside the response envelope.</summary>
    [JsonPropertyName("error")]
    public OpenAiBatchOutputError? Error { get; init; }
}

/// <summary>The HTTP envelope of one OpenAI batch output line.</summary>
internal sealed class OpenAiBatchOutputResponse
{
    /// <summary>The HTTP status code of the request.</summary>
    [JsonPropertyName("status_code")]
    public int? StatusCode { get; init; }

    /// <summary>The provider request identifier, when reported.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>
    /// The response body: a chat completion on success, or an OpenAI error
    /// object on failure.
    /// </summary>
    [JsonPropertyName("body")]
    public JsonElement Body { get; init; }
}

/// <summary>The OpenAI error shape reported for a failed batch item.</summary>
internal sealed class OpenAiBatchOutputError
{
    /// <summary>The human-readable error description.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>The error type, for example <c>invalid_request_error</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The error code, when one is reported.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}
