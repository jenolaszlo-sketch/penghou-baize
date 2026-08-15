namespace Penghou.Baize.Generation;

/// <summary>
/// The exception thrown when a generation operation fails before it is
/// accepted by a provider — validation failures, HTTP 401/429, connection
/// failures before the request body is sent — or when the outcome of a
/// submission is ambiguous. After acceptance, provider-side job failures are
/// represented as <see cref="GenerationError"/> on the
/// <see cref="GenerationOperation"/> instead of an exception.
/// </summary>
public sealed class BaizeException : Exception
{
    /// <summary>The normalized failure classification.</summary>
    public GenerationErrorKind ErrorKind { get; }

    /// <summary>The provider HTTP status code, when one was reported.</summary>
    public int? StatusCode { get; }

    /// <summary>The raw provider-side error text, when one was reported.</summary>
    public string? ProviderStatus { get; }

    /// <summary>Initializes a new instance of the generation exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorKind">The normalized failure classification.</param>
    /// <param name="statusCode">The provider HTTP status code, when reported.</param>
    /// <param name="providerStatus">Raw provider-side error text, when reported.</param>
    /// <param name="innerException">The exception that caused this failure, when present.</param>
    public BaizeException(
        string message,
        GenerationErrorKind errorKind,
        int? statusCode = null,
        string? providerStatus = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorKind = errorKind;
        StatusCode = statusCode;
        ProviderStatus = providerStatus;
    }

    /// <summary>Creates an <see cref="GenerationErrorKind.UnsupportedCapability"/> exception.</summary>
    /// <param name="message">The error message.</param>
    /// <returns>The exception.</returns>
    public static BaizeException UnsupportedCapability(string message) =>
        new(message, GenerationErrorKind.UnsupportedCapability);

    /// <summary>Creates an <see cref="GenerationErrorKind.InvalidRequest"/> exception.</summary>
    /// <param name="message">The error message.</param>
    /// <returns>The exception.</returns>
    public static BaizeException InvalidRequest(string message) =>
        new(message, GenerationErrorKind.InvalidRequest);

    /// <summary>Creates an <see cref="GenerationErrorKind.UnknownSubmissionOutcome"/> exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying transport exception, when present.</param>
    /// <returns>The exception.</returns>
    public static BaizeException UnknownSubmissionOutcome(
        string message,
        Exception? innerException = null) =>
        new(message, GenerationErrorKind.UnknownSubmissionOutcome, innerException: innerException);

    /// <summary>Creates a <see cref="GenerationErrorKind.ProviderUnavailable"/> exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying transport exception, when present.</param>
    /// <returns>The exception.</returns>
    public static BaizeException ProviderUnavailable(
        string message,
        Exception? innerException = null) =>
        new(message, GenerationErrorKind.ProviderUnavailable, innerException: innerException);

    /// <summary>
    /// Classifies a provider HTTP status code into a
    /// <see cref="GenerationErrorKind"/>. Providers that expose a richer error
    /// vocabulary in the response body refine this via their own mapping.
    /// </summary>
    /// <param name="statusCode">The provider HTTP status code.</param>
    /// <returns>The normalized failure classification.</returns>
    public static GenerationErrorKind ClassifyStatusCode(int statusCode) =>
        statusCode switch
        {
            401 => GenerationErrorKind.Authentication,
            403 => GenerationErrorKind.Authorization,
            429 => GenerationErrorKind.RateLimited,
            402 => GenerationErrorKind.QuotaExceeded,
            408 => GenerationErrorKind.ProviderUnavailable,
            400 or 404 or 405 or 422 => GenerationErrorKind.InvalidRequest,
            >= 500 => GenerationErrorKind.ProviderUnavailable,
            _ => GenerationErrorKind.InvalidRequest
        };
}