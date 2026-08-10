namespace Penghou.Baize.Batch;

/// <summary>
/// Limits applied by <see cref="BatchPlanner"/> when grouping logical requests
/// into physical provider batches.
/// </summary>
public sealed record BatchPlannerOptions
{
    /// <summary>
    /// The maximum number of requests per physical provider batch. Groups larger
    /// than this are split into consecutive sub-groups, preserving order. Null
    /// (the default) leaves grouping unlimited.
    /// </summary>
    public int? MaxItemsPerGroup { get; init; }
}
