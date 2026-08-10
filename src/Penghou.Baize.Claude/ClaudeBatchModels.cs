using System.Text.Json.Serialization;

namespace Penghou.Baize.Claude;

/// <summary>
/// Wire model for an Anthropic Messages Batch object, as returned by
/// <c>POST /v1/messages/batches</c> and <c>GET /v1/messages/batches/{id}</c>.
/// </summary>
internal sealed class ClaudeMessageBatch
{
    /// <summary>The batch identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// The batch lifecycle status (<c>in_progress</c>, <c>processing</c>,
    /// <c>ended</c>, <c>canceled</c> or <c>expired</c>).
    /// </summary>
    [JsonPropertyName("processing_status")]
    public string? ProcessingStatus { get; init; }

    /// <summary>Per-request completion counts, when reported.</summary>
    [JsonPropertyName("request_counts")]
    public ClaudeMessageBatchRequestCounts? RequestCounts { get; init; }

    /// <summary>Client-supplied metadata echoed back by the API, when any.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>Per-request completion counts for an Anthropic messages batch.</summary>
internal sealed class ClaudeMessageBatchRequestCounts
{
    /// <summary>Total requests in the batch.</summary>
    [JsonPropertyName("total")]
    public int? Total { get; init; }

    /// <summary>Requests that have been processed.</summary>
    [JsonPropertyName("processed")]
    public int? Processed { get; init; }

    /// <summary>Requests that succeeded.</summary>
    [JsonPropertyName("succeeded")]
    public int? Succeeded { get; init; }

    /// <summary>Requests that errored.</summary>
    [JsonPropertyName("errored")]
    public int? Errored { get; init; }

    /// <summary>Requests that were cancelled.</summary>
    [JsonPropertyName("canceled")]
    public int? Canceled { get; init; }

    /// <summary>Requests that expired.</summary>
    [JsonPropertyName("expired")]
    public int? Expired { get; init; }
}

/// <summary>
/// Wire model for the request body of <c>POST /v1/messages/batches</c>.
/// </summary>
internal sealed class ClaudeMessageBatchCreateRequest
{
    /// <summary>The individual requests, each with a stable correlation id.</summary>
    [JsonPropertyName("requests")]
    public List<ClaudeMessageBatchRequestItem>? Requests { get; init; }

    /// <summary>Client-supplied metadata attached to the batch.</summary>
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>One request inside an Anthropic messages batch.</summary>
internal sealed class ClaudeMessageBatchRequestItem
{
    /// <summary>The stable caller-supplied correlation id.</summary>
    [JsonPropertyName("custom_id")]
    public required string CustomId { get; init; }

    /// <summary>The Messages API request parameters.</summary>
    [JsonPropertyName("params")]
    public required ClaudeMessageRequest Params { get; init; }
}

/// <summary>
/// Wire model for one JSONL line of an Anthropic batch results file
/// (<c>GET /v1/messages/batches/{id}/results</c>).
/// </summary>
internal sealed class ClaudeMessageBatchResultLine
{
    /// <summary>The correlation id echoed from the submitted item.</summary>
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; init; }

    /// <summary>The per-request outcome.</summary>
    [JsonPropertyName("result")]
    public ClaudeMessageBatchResult? Result { get; init; }
}

/// <summary>
/// The outcome of one request in an Anthropic messages batch. <see cref="Type"/>
/// is <c>succeeded</c>, <c>errored</c>, <c>canceled</c> or <c>expired</c>.
/// </summary>
internal sealed class ClaudeMessageBatchResult
{
    /// <summary>The result type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The non-streaming Messages response, when the request succeeded.</summary>
    [JsonPropertyName("message")]
    public ClaudeMessageResponse? Message { get; init; }

    /// <summary>The normalized error, when the request errored.</summary>
    [JsonPropertyName("error")]
    public ClaudeBatchError? Error { get; init; }
}

/// <summary>The Anthropic error shape reported for a failed batch request.</summary>
internal sealed class ClaudeBatchError
{
    /// <summary>The error type (for example <c>invalid_request_error</c>).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The human-readable error description.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
