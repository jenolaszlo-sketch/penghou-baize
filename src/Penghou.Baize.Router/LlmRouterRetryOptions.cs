namespace Penghou.Baize.Router;

/// <summary>
/// Configures bounded retries after every compatible endpoint in a route has
/// failed transiently before producing content or tool output.
/// </summary>
public sealed record LlmRouterRetryOptions
{
    /// <summary>Default bounded retry behavior.</summary>
    public static LlmRouterRetryOptions Default { get; } = new();

    /// <summary>
    /// Maximum number of passes through the route. One disables same-route
    /// retries while preserving fallback between different endpoints.
    /// </summary>
    public int MaximumAttempts { get; init; } = 2;

    /// <summary>Delay before the second route pass.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Multiplier applied to each later retry delay.</summary>
    public double BackoffFactor { get; init; } = 2;

    /// <summary>Upper bound for an automatically selected retry delay.</summary>
    public TimeSpan MaximumDelay { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaximumAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumAttempts));
        if (InitialDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(InitialDelay));
        if (BackoffFactor < 1 ||
            double.IsNaN(BackoffFactor) ||
            double.IsInfinity(BackoffFactor))
            throw new ArgumentOutOfRangeException(nameof(BackoffFactor));
        if (MaximumDelay < InitialDelay)
            throw new ArgumentOutOfRangeException(nameof(MaximumDelay));
    }

    internal TimeSpan DelayForRetry(
        int completedAttempts,
        TimeSpan? providerRetryAfter)
    {
        var exponentialMilliseconds = InitialDelay.TotalMilliseconds *
            Math.Pow(BackoffFactor, Math.Max(0, completedAttempts - 1));
        var delay = TimeSpan.FromMilliseconds(
            Math.Min(exponentialMilliseconds, MaximumDelay.TotalMilliseconds));

        if (providerRetryAfter is { } hint && hint > delay)
            delay = hint > MaximumDelay ? MaximumDelay : hint;

        return delay;
    }
}
