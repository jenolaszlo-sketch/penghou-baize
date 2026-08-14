namespace Penghou.Baize.Generation;

/// <summary>
/// The normalized failure vocabulary for artifact generation. This is finer than
/// <see cref="LlmClientFailureKind"/> because generation must distinguish, for
/// example, a capability that is simply absent from a request that was rejected,
/// and an ambiguous submission from a definitively failed one, before any
/// retry policy can be applied.
/// </summary>
public enum GenerationErrorKind
{
    /// <summary>The request itself was invalid.</summary>
    InvalidRequest,

    /// <summary>The endpoint does not support the requested capability.</summary>
    UnsupportedCapability,

    /// <summary>Authentication failed (for example HTTP 401).</summary>
    Authentication,

    /// <summary>The caller is authenticated but not authorized (for example HTTP 403).</summary>
    Authorization,

    /// <summary>A billed quota was exceeded.</summary>
    QuotaExceeded,

    /// <summary>The endpoint is rate limiting the caller (for example HTTP 429).</summary>
    RateLimited,

    /// <summary>The provider rejected the request on safety grounds.</summary>
    SafetyRejected,

    /// <summary>The provider or network was unavailable before acceptance.</summary>
    ProviderUnavailable,

    /// <summary>A provider-side job failed after the operation was accepted.</summary>
    GenerationFailed,

    /// <summary>
    /// A provider-side operation was canceled (for example by moderation, quota
    /// policy, or a provider-side control plane action). Distinct from a local
    /// <see cref="OperationCanceledException"/> from the caller's cancellation
    /// token, which never cancels the provider operation by itself.
    /// </summary>
    Canceled,

    /// <summary>
    /// A submission was attempted but its outcome is unknown. Submission HTTP
    /// failures with an ambiguous outcome MUST surface this so the caller can
    /// decide whether to replay an expensive request, never a blind automatic
    /// retry.
    /// </summary>
    UnknownSubmissionOutcome,

    /// <summary>
    /// A queued operation did not reach a terminal state within the executor's
    /// configured timeout. The operation may still be running on the provider;
    /// the caller can resume from the pinned handle.
    /// </summary>
    TimeoutExceeded
}