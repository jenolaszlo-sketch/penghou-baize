namespace Penghou.Baize.Batch;

/// <summary>
/// The normalized aggregate status of a logical batch, combining the statuses of
/// its physical provider batches.
/// </summary>
/// <param name="LogicalBatchId">The stable logical batch identifier.</param>
/// <param name="State">The normalized aggregate batch state.</param>
/// <param name="Total">The total number of logical requests.</param>
/// <param name="Completed">The number of logical requests completed.</param>
/// <param name="Failed">The number of logical requests failed.</param>
/// <param name="Parts">The physical parts and provider statuses the aggregate was derived from.</param>
public sealed record BaizeBatchStatus(
    string LogicalBatchId,
    BaizeBatchState State,
    int Total,
    int Completed,
    int Failed,
    IReadOnlyList<ProviderBatchPartStatus>? Parts = null);
