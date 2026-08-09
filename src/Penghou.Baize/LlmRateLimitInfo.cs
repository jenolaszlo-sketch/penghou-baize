namespace Penghou.Baize;

/// <summary>
/// Rate-limit and quota information reported by a provider, derived from
/// response headers (OpenAI <c>x-ratelimit-*</c>, Anthropic
/// <c>anthropic-ratelimit-*</c>) and retry hints (the <c>Retry-After</c>
/// header or Anthropic's <c>retry_after</c> body field). Drives circuit
/// breaking and endpoint exclusion from routing.
/// </summary>
/// <param name="RequestsRemaining">Requests remaining in the current window, when reported.</param>
/// <param name="RequestsLimit">The requests-per-minute cap, when reported.</param>
/// <param name="RequestsResetAt">When the request budget refills, when reported.</param>
/// <param name="TokensRemaining">Tokens remaining in the current window, when reported.</param>
/// <param name="TokensLimit">The tokens-per-minute cap, when reported.</param>
/// <param name="TokensResetAt">When the token budget refills, when reported.</param>
/// <param name="RetryAfter">A retry hint, when reported.</param>
public sealed record LlmRateLimitInfo(
    int? RequestsRemaining = null,
    int? RequestsLimit = null,
    DateTimeOffset? RequestsResetAt = null,
    int? TokensRemaining = null,
    int? TokensLimit = null,
    DateTimeOffset? TokensResetAt = null,
    TimeSpan? RetryAfter = null)
{
    /// <summary>
    /// The earliest instant the endpoint is expected to accept requests
    /// again, derived from the reported reset times and any retry hint;
    /// null when nothing was reported.
    /// </summary>
    public DateTimeOffset? UnavailableUntil
    {
        get
        {
            DateTimeOffset? result = null;

            if (RequestsResetAt is { } requestsReset &&
                (result is null || requestsReset > result))
            {
                result = requestsReset;
            }

            if (TokensResetAt is { } tokensReset &&
                (result is null || tokensReset > result))
            {
                result = tokensReset;
            }

            if (RetryAfter is { } retryAfter)
            {
                var fromRetryAfter = DateTimeOffset.UtcNow + retryAfter;

                if (result is null || fromRetryAfter > result)
                {
                    result = fromRetryAfter;
                }
            }

            return result;
        }
    }
}
