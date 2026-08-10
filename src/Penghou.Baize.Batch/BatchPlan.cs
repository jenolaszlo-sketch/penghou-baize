namespace Penghou.Baize.Batch;

/// <summary>
/// The serializable result of batch planning: a logical batch split into
/// physical provider batches. The plan is deterministic — it depends only on the
/// configured routes, never on live routing state — so it can be persisted and
/// replayed by an orchestration runtime.
/// </summary>
/// <param name="LogicalBatchId">The stable logical batch identifier.</param>
/// <param name="Groups">The physical provider batches the logical requests were grouped into.</param>
public sealed record BatchPlan(
    string LogicalBatchId,
    IReadOnlyList<ProviderBatchGroup> Groups);
