using Penghou.Baize;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Classifies Gemini errors into the normalized
/// <see cref="LlmClientFailureKind"/> vocabulary. Uses the canonical REST status
/// (for example <c>RESOURCE_EXHAUSTED</c>) when present and falls back to the
/// HTTP status code so per-item batch failures classify consistently with
/// direct-call HTTP failures.
/// </summary>
internal static class GeminiErrorClassifier
{
    /// <summary>
    /// Classifies a Gemini error into an <see cref="LlmClientFailureKind"/>.
    /// </summary>
    /// <param name="status">The canonical REST status, when reported.</param>
    /// <param name="statusCode">The HTTP status code, when reported.</param>
    /// <returns>The normalized failure classification.</returns>
    public static LlmClientFailureKind ClassifyFailureKind(
        string? status,
        int? statusCode)
    {
        var byStatus = ClassifyByStatus(status);

        if (byStatus is not null)
            return byStatus.Value;

        return statusCode is { } code
            ? LlmClientException.ClassifyStatusCode(code)
            : LlmClientFailureKind.Protocol;
    }

    private static LlmClientFailureKind? ClassifyByStatus(string? status) =>
        status switch
        {
            "UNAUTHENTICATED" or "PERMISSION_DENIED" =>
                LlmClientFailureKind.Authentication,
            "RESOURCE_EXHAUSTED" or "QUOTA_EXCEEDED" or "RATE_LIMIT_EXCEEDED" =>
                LlmClientFailureKind.RateLimit,
            "INVALID_ARGUMENT" or "NOT_FOUND" or "FAILED_PRECONDITION" or
            "OUT_OF_RANGE" or "UNIMPLEMENTED" =>
                LlmClientFailureKind.InvalidRequest,
            "UNAVAILABLE" or "DEADLINE_EXCEEDED" or "ABORTED" or
            "INTERNAL" or "CANCELLED" or "DATA_LOSS" or "UNKNOWN" =>
                LlmClientFailureKind.Availability,
            _ => null
        };
}
