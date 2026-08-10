namespace Penghou.Baize;

/// <summary>
/// Represents a failure while calling an LLM provider, including failures
/// caused by non-successful HTTP responses.
/// </summary>
public sealed class LlmClientException : Exception
{
    /// <summary>
    /// The HTTP status code of the failing response, when the failure
    /// originated from a non-successful HTTP response.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Rate-limit and quota information captured from the failing response,
    /// when the provider reported any.
    /// </summary>
    public LlmRateLimitInfo? RateLimit { get; }

    /// <summary>
    /// The classification of the failure. Derived from the HTTP status code
    /// when the failure came from an HTTP response, otherwise set explicitly
    /// by the provider adapter that raised it (for example an in-stream
    /// <c>overloaded_error</c> or a stream that ended early).
    /// </summary>
    public LlmClientFailureKind FailureKind { get; }

    /// <summary>
    /// Whether the request could plausibly succeed if reissued, either on the
    /// same endpoint or on a different one. True for transient failures
    /// (availability and rate limits), false when the failure is deterministic
    /// (authentication, invalid requests, and content rejections).
    /// </summary>
    public bool CanFallback { get; }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The error message.</param>
    public LlmClientException(string message) : base(message)
    {
        (FailureKind, CanFallback) = Classify(statusCode: null, kind: null);
    }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public LlmClientException(string message, Exception innerException)
        : base(message, innerException)
    {
        (FailureKind, CanFallback) = Classify(statusCode: null, kind: null);
    }

    /// <summary>Initializes a new instance with a message, status code, and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code, when available.</param>
    /// <param name="innerException">The exception that caused this failure, when present.</param>
    public LlmClientException(
        string message,
        int? statusCode,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        (FailureKind, CanFallback) = Classify(statusCode, kind: null);
    }

    /// <summary>Initializes a new instance with a message, status code, and rate-limit info.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code, when available.</param>
    /// <param name="rateLimit">Rate-limit and quota information, when reported.</param>
    /// <param name="innerException">The exception that caused this failure, when present.</param>
    public LlmClientException(
        string message,
        int? statusCode,
        LlmRateLimitInfo? rateLimit,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RateLimit = rateLimit;
        (FailureKind, CanFallback) = Classify(statusCode, kind: null);
    }

    /// <summary>
    /// Initializes a new instance with an explicit failure classification,
    /// for failures that did not originate from an HTTP response.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="failureKind">The classification of the failure.</param>
    /// <param name="statusCode">The HTTP status code, when available.</param>
    /// <param name="rateLimit">Rate-limit and quota information, when reported.</param>
    /// <param name="canFallback">
    /// Whether the request could succeed if reissued. Defaults to true for
    /// availability and rate-limit failures and false otherwise.
    /// </param>
    /// <param name="innerException">The exception that caused this failure, when present.</param>
    public LlmClientException(
        string message,
        LlmClientFailureKind failureKind,
        int? statusCode = null,
        LlmRateLimitInfo? rateLimit = null,
        bool? canFallback = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        StatusCode = statusCode;
        RateLimit = rateLimit;
        CanFallback = canFallback ?? AllowsFallback(failureKind);
    }

    private static (LlmClientFailureKind Kind, bool CanFallback) Classify(
        int? statusCode,
        LlmClientFailureKind? kind)
    {
        var failureKind = kind ?? ClassifyStatusCode(statusCode);
        return (failureKind, AllowsFallback(failureKind));
    }

    /// <summary>
    /// Classifies a provider HTTP status code into an
    /// <see cref="LlmClientFailureKind"/> using the same mapping applied to
    /// failed HTTP responses. Reused by the asynchronous batch adapters so
    /// per-item batch failures are classified identically to direct calls.
    /// </summary>
    /// <param name="statusCode">The provider HTTP status code.</param>
    /// <returns>The normalized failure classification.</returns>
    public static LlmClientFailureKind ClassifyStatusCode(int statusCode) =>
        statusCode switch
        {
            401 or 403 => LlmClientFailureKind.Authentication,
            429 => LlmClientFailureKind.RateLimit,
            400 or 404 or 405 or 422 => LlmClientFailureKind.InvalidRequest,
            408 or >= 500 => LlmClientFailureKind.Availability,
            _ => LlmClientFailureKind.Protocol
        };

    private static LlmClientFailureKind ClassifyStatusCode(int? statusCode) =>
        statusCode is { } value
            ? ClassifyStatusCode(value)
            : LlmClientFailureKind.Protocol;

    private static bool AllowsFallback(LlmClientFailureKind kind) =>
        kind is LlmClientFailureKind.Availability or LlmClientFailureKind.RateLimit;
}