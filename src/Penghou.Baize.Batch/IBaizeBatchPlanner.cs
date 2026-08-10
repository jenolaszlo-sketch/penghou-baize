namespace Penghou.Baize.Batch;

/// <summary>
/// Groups logical requests into physical provider batches before submission.
/// The planner routes each request to an endpoint, resolves its batch
/// capabilities, groups compatible requests per provider, and splits groups
/// according to configured limits.
/// </summary>
public interface IBaizeBatchPlanner
{
    /// <summary>
    /// Plans a logical batch into physical provider batches.
    /// </summary>
    /// <param name="submission">The logical batch to plan.</param>
    /// <returns>The serializable execution plan.</returns>
    /// <exception cref="BatchPlanException">
    /// Thrown when a request cannot be routed or routes to an endpoint without
    /// native batch support.
    /// </exception>
    BatchPlan Plan(BaizeBatchSubmission submission);
}
