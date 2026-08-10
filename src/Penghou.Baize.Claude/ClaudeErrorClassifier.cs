using Penghou.Baize;

namespace Penghou.Baize.Claude;

/// <summary>
/// Classifies Anthropic error types into the normalized
/// <see cref="LlmClientFailureKind"/> vocabulary. Shared by the streaming and
/// batch clients so in-stream and per-item failures classify identically.
/// </summary>
internal static class ClaudeErrorClassifier
{
    /// <summary>
    /// Classifies an Anthropic error type into an
    /// <see cref="LlmClientFailureKind"/>.
    /// </summary>
    /// <param name="errorType">The raw Anthropic error type.</param>
    /// <returns>The normalized failure classification.</returns>
    public static LlmClientFailureKind ClassifyFailureKind(
        string errorType) =>
        errorType switch
        {
            "authentication_error" or "permission_error" =>
                LlmClientFailureKind.Authentication,
            "rate_limit_error" =>
                LlmClientFailureKind.RateLimit,
            "invalid_request_error" or "not_found_error" or
            "request_too_large_error" or "context_length_error" or
            "unsupported_country_error" =>
                LlmClientFailureKind.InvalidRequest,
            "overloaded_error" or "api_error" or "timeout_error" or
            "connection_error" =>
                LlmClientFailureKind.Availability,
            _ => LlmClientFailureKind.Protocol
        };
}
