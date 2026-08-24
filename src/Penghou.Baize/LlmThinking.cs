namespace Penghou.Baize;

/// <summary>
/// Shared thinking/reasoning-effort mapping for providers whose wire vocabulary
/// is <c>low|medium|high</c>. Providers without a "max" tier must reject it
/// rather than silently cap, since capping changes billing and latency
/// characteristics without the caller's consent.
/// </summary>
public static class LlmThinking
{
    /// <summary>
    /// Maps a neutral thinking effort onto the standard three-tier wire
    /// vocabulary, or null when no effort should be sent.
    /// </summary>
    /// <param name="providerDisplayName">
    /// The provider name used in the rejection message for unsupported "max".
    /// </param>
    /// <param name="effort">The caller-requested effort.</param>
    public static string? MapStandardEffort(
        string providerDisplayName,
        LlmThinkingEffort effort) =>
        effort switch
        {
            LlmThinkingEffort.None => null,
            LlmThinkingEffort.Low => "low",
            LlmThinkingEffort.Medium => "medium",
            LlmThinkingEffort.High => "high",
            // No "max" tier on this wire; reject rather than silently capping.
            LlmThinkingEffort.Max => throw new LlmRequestValidationException(
                $"{providerDisplayName} does not support a 'max' reasoning effort; it would " +
                "be silently capped to 'high'."),
            _ => null
        };
}
