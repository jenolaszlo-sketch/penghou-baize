namespace Penghou.Baize.Gemini;

/// <summary>
/// Response-mapping primitives shared by the Gemini streaming chat client and
/// the native batch client: finish-reason vocabulary, token arithmetic, usage
/// projection, and reasoning-continuation construction.
/// </summary>
internal static class GeminiResponseMapping
{
    /// <summary>Maps a Gemini finish reason onto the neutral finish vocabulary.</summary>
    public static string MapFinishReason(string finishReason) =>
        finishReason switch
        {
            "STOP" => "stop",
            "MAX_OUTPUT_TOKENS" => "length",
            "SAFETY" => "content_filter",
            _ => finishReason.ToLowerInvariant()
        };

    /// <summary>
    /// Gemini reports candidate tokens and thinking tokens separately; the
    /// neutral completion count is their sum. Null only when both are absent.
    /// </summary>
    public static int? SumGeneratedTokens(int? candidates, int? thoughts) =>
        candidates.HasValue || thoughts.HasValue
            ? candidates.GetValueOrDefault() + thoughts.GetValueOrDefault()
            : null;

    /// <summary>Projects Gemini usage into the neutral usage shape.</summary>
    public static LlmUsage ToLlmUsage(GeminiUsage usage) =>
        new(
            PromptTokens: usage.PromptTokenCount,
            CompletionTokens: SumGeneratedTokens(
                usage.CandidatesTokenCount,
                usage.ThoughtsTokenCount),
            TotalTokens: usage.TotalTokenCount,
            ThinkingTokens: usage.ThoughtsTokenCount);

    /// <summary>
    /// Builds the reasoning continuation carrying a part's thought signature,
    /// or null when the part carries none.
    /// </summary>
    public static LlmProviderContinuation? ContinuationFor(string? thoughtSignature) =>
        thoughtSignature is null
            ? null
            : new LlmProviderContinuation(
                Provider: "Gemini",
                Values: new Dictionary<string, string>
                {
                    ["thoughtSignature"] = thoughtSignature
                });
}
