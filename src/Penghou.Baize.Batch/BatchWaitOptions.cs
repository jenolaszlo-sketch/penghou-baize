namespace Penghou.Baize.Batch;

/// <summary>Controls polling while waiting for a logical batch to finish.</summary>
public sealed record BatchWaitOptions
{
    /// <summary>Delay between provider status checks. Defaults to five seconds.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum adaptive polling interval. Defaults to one minute.</summary>
    public TimeSpan MaxPollInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Multiplier applied after each non-terminal poll. Defaults to 1.5;
    /// use 1 for a fixed interval.
    /// </summary>
    public double BackoffFactor { get; init; } = 1.5;

    /// <summary>
    /// Symmetric randomization applied to delays, from 0 through 1. Defaults
    /// to 0.1 (plus or minus ten percent); use 0 for deterministic polling.
    /// </summary>
    public double JitterRatio { get; init; } = 0.1;

    /// <summary>
    /// Number of consecutive transient status failures tolerated before the
    /// wait fails. Defaults to three.
    /// </summary>
    public int MaxTransientFailures { get; init; } = 3;

    /// <summary>Optional overall wait timeout. Null waits until cancellation.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Optional observer for status, retry, and next-delay updates.</summary>
    public IProgress<BatchPollingUpdate>? Progress { get; init; }
}
