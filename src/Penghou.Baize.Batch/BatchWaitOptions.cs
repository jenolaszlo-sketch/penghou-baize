namespace Penghou.Baize.Batch;

/// <summary>Controls polling while waiting for a logical batch to finish.</summary>
public sealed record BatchWaitOptions
{
    /// <summary>Delay between provider status checks. Defaults to five seconds.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Optional overall wait timeout. Null waits until cancellation.</summary>
    public TimeSpan? Timeout { get; init; }
}
