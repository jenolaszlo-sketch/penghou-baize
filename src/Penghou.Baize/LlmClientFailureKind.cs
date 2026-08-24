namespace Penghou.Baize;

/// <summary>
/// Classifies why a call to an LLM provider failed. Known regardless of
/// whether an HTTP status code accompanied the failure, so the router can
/// decide whether a broken call can be retried on another endpoint purely
/// from the exception's classification.
/// </summary>
public enum LlmClientFailureKind
{
    /// <summary>
    /// The provider was unreachable or reported a transient server-side
    /// problem: a connection failure, a timeout, an HTTP 5xx, an in-stream
    /// overloaded_error, or a stream that ended without a final response.
    /// Repeating the request may succeed.
    /// </summary>
    Availability,

    /// <summary>
    /// The provider refused the request because a rate or quota limit was
    /// reached (HTTP 429, retry-after hints, in-stream rate limit errors).
    /// </summary>
    RateLimit,

    /// <summary>
    /// The request failed authentication (HTTP 401): the credentials are
    /// missing, malformed, or rejected. Retrying with the same credentials
    /// cannot succeed.
    /// </summary>
    Authentication,

    /// <summary>
    /// The request was authorized against valid credentials but denied
    /// permission (HTTP 403): the key may not access the model or resource.
    /// Retrying cannot succeed without an entitlement change.
    /// </summary>
    Authorization,

    /// <summary>
    /// The request itself was rejected as invalid (HTTP 400/404/405 for
    /// unsupported parameters, missing models, context-length limits).
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// The client-provider protocol broke: malformed JSON, an unparseable
    /// stream, or an unexpected event shape.
    /// </summary>
    Protocol,

    /// <summary>
    /// The provider produced output that cannot be surfaced (for example a
    /// content-filter rejection or an unusable response body).
    /// </summary>
    Content
}