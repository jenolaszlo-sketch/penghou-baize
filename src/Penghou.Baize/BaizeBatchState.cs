namespace Penghou.Baize;

/// <summary>
/// The normalized lifecycle state of an asynchronous batch, mapped from the
/// provider's own status vocabulary. A mixed-provider logical batch derives its
/// state from its physical provider batches.
/// </summary>
public enum BaizeBatchState
{
    /// <summary>The batch was submitted but has not started processing.</summary>
    Pending,

    /// <summary>The batch is actively processing.</summary>
    Running,

    /// <summary>Every request in the batch completed successfully.</summary>
    Completed,

    /// <summary>Some requests completed and others failed or were cancelled.</summary>
    PartiallyCompleted,

    /// <summary>The whole batch failed.</summary>
    Failed,

    /// <summary>A cancellation was requested and is being applied.</summary>
    Cancelling,

    /// <summary>The batch was cancelled before completing.</summary>
    Cancelled,

    /// <summary>The batch expired before completing.</summary>
    Expired
}
