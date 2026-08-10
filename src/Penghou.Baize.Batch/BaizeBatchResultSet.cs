namespace Penghou.Baize.Batch;

/// <summary>
/// The normalized results of a logical batch after every provider part has
/// completed.
/// </summary>
/// <param name="LogicalBatchId">The stable logical batch identifier.</param>
/// <param name="State">The final aggregate batch state.</param>
/// <param name="Results">The per-request results, correlated by <see cref="BaizeBatchResult.RequestId"/>.</param>
public sealed record BaizeBatchResultSet(
    string LogicalBatchId,
    BaizeBatchState State,
    IReadOnlyList<BaizeBatchResult> Results);
