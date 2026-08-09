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

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The error message.</param>
    public LlmClientException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public LlmClientException(string message, Exception innerException)
        : base(message, innerException)
    {
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
    }
}
