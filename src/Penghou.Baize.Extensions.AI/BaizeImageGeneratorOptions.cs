namespace Penghou.Baize.Extensions.AI;

/// <summary>
/// Polling behaviour for <see cref="BaizeImageGenerator"/> when the adapted
/// provider accepts image generation asynchronously (queued providers).
/// </summary>
public sealed record BaizeImageGeneratorOptions
{
    /// <summary>How long to wait between operation-status polls. Defaults to 2 seconds.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The maximum time to wait for a queued operation to reach a terminal
    /// state before giving up. The exception message carries the operation
    /// handle so the caller can resume later. <c>null</c> waits indefinitely.
    /// Defaults to 10 minutes.
    /// </summary>
    public TimeSpan? Timeout { get; init; } = TimeSpan.FromMinutes(10);
}
