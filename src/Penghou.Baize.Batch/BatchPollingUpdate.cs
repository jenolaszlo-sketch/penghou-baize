namespace Penghou.Baize.Batch;

/// <summary>A progress update emitted while waiting for a logical batch.</summary>
/// <param name="PollNumber">The one-based status attempt number.</param>
/// <param name="Status">The latest aggregate status, when polling succeeded.</param>
/// <param name="ConsecutiveTransientFailures">Current transient failure count.</param>
/// <param name="NextDelay">Delay before the next attempt, when another is planned.</param>
/// <param name="Error">The transient error message, when an attempt failed.</param>
public sealed record BatchPollingUpdate(
    int PollNumber,
    BaizeBatchStatus? Status,
    int ConsecutiveTransientFailures,
    TimeSpan? NextDelay = null,
    string? Error = null);
