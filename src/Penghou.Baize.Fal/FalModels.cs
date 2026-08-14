using System.Text.Json.Serialization;

namespace Penghou.Baize.Fal;

/// <summary>
/// The response returned by a fal.ai queue submission
/// (<c>POST {base}/{model}</c>). fal runs on the queue asynchronously: the
/// submission returns a request id immediately, and callers poll
/// <c>GET {base}/requests/{id}/status</c> until <c>COMPLETED</c>.
/// </summary>
/// <param name="RequestId">The provider-assigned request id used for status, result, and cancellation.</param>
/// <param name="Status">The queue status snapshot at submission time.</param>
/// <param name="ResponseUrl">The URL that serves the final result, when provided.</param>
/// <param name="StatusUrl">The URL that serves status snapshots, when provided.</param>
/// <param name="CancelUrl">The URL that cancels the request, when provided.</param>
public sealed record FalQueueResponse(
    [property: JsonPropertyName("request_id")] string? RequestId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("response_url")] string? ResponseUrl,
    [property: JsonPropertyName("status_url")] string? StatusUrl,
    [property: JsonPropertyName("cancel_url")] string? CancelUrl);

/// <summary>
/// A status snapshot for a queued fal request
/// (<c>GET {base}/requests/{id}/status</c>). fal reports a <c>position</c> in
/// the queue rather than a completion fraction, so the client surfaces the
/// position in provider metadata and reports no numeric progress.
/// </summary>
/// <param name="RequestId">The provider-assigned request id.</param>
/// <param name="Status">The queue status: <c>IN_QUEUE</c>, <c>IN_PROGRESS</c>, <c>COMPLETED</c>, or <c>CANCELED</c>.</param>
/// <param name="Position">The 1-based queue position while <c>IN_QUEUE</c>, when reported.</param>
/// <param name="Metrics">Timing metrics for the request, when reported.</param>
public sealed record FalRequestStatus(
    [property: JsonPropertyName("request_id")] string? RequestId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("position")] int? Position,
    [property: JsonPropertyName("metrics")] FalRequestMetrics? Metrics);

/// <summary>Timing metrics reported for a fal queue request.</summary>
/// <param name="QueueTime">Seconds spent waiting in the queue.</param>
/// <param name="InferenceTime">Seconds spent running inference.</param>
/// <param name="TotalTime">Total elapsed seconds.</param>
public sealed record FalRequestMetrics(
    [property: JsonPropertyName("queue_time")] double? QueueTime,
    [property: JsonPropertyName("inference_time")] double? InferenceTime,
    [property: JsonPropertyName("total_time")] double? TotalTime);
