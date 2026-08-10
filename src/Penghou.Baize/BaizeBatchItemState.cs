namespace Penghou.Baize;

/// <summary>The normalized outcome of a single request inside an asynchronous batch.</summary>
public enum BaizeBatchItemState
{
    /// <summary>The request has not produced a result yet.</summary>
    Pending,

    /// <summary>The request completed and produced a response.</summary>
    Succeeded,

    /// <summary>The request failed.</summary>
    Failed,

    /// <summary>The request was cancelled before completing.</summary>
    Cancelled,

    /// <summary>The request expired before completing.</summary>
    Expired
}
