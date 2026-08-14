namespace Penghou.Baize.Generation;

/// <summary>
/// Configures the in-process <see cref="IGenerationExecutor"/> polling loop.
/// </summary>
public sealed class GenerationExecutorOptions
{
    /// <summary>
    /// The maximum total time the executor waits for a queued operation to reach
    /// a terminal state. When exceeded, the executor throws a
    /// <see cref="BaizeException"/> with
    /// <see cref="GenerationErrorKind.TimeoutExceeded"/> and the operation handle
    /// in the message so the caller can resume later. Defaults to 10 minutes.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The delay before the first status poll. Defaults to 1 second.</summary>
    public TimeSpan InitialPollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The maximum delay between status polls after backoff growth. Defaults to
    /// 30 seconds.
    /// </summary>
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The multiplier applied to the polling interval after each non-terminal
    /// poll, bounded by <see cref="MaxPollingInterval"/>. Defaults to 2.0.
    /// </summary>
    public double PollingBackoffMultiplier { get; set; } = 2.0;
}
