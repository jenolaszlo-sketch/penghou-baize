namespace Penghou.Baize;

/// <summary>
/// The asynchronous batch operations an endpoint supports. Batch support is a
/// per-endpoint capability resolved from the configured model endpoint; an
/// OpenAI-compatible API style does not automatically imply an OpenAI-compatible
/// batch endpoint.
/// </summary>
[Flags]
public enum BatchCapabilities
{
    /// <summary>The endpoint does not support asynchronous batching.</summary>
    None = 0,

    /// <summary>The endpoint natively accepts a group of requests for later processing.</summary>
    NativeBatch = 1,

    /// <summary>The endpoint reports asynchronous batch status that can be polled.</summary>
    Polling = 2,

    /// <summary>The endpoint can cancel a running batch.</summary>
    Cancellation = 4,

    /// <summary>The endpoint may expose results before the whole batch completes.</summary>
    PartialResults = 8
}
