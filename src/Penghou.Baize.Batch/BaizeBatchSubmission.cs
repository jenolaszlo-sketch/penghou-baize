namespace Penghou.Baize.Batch;

/// <summary>
/// A logical batch of requests, possibly spanning multiple models and providers.
/// </summary>
/// <param name="Requests">The logical requests to execute.</param>
/// <param name="Id">
/// A stable caller-supplied logical batch identifier. When omitted, the planner
/// generates one. Submission idempotency is keyed on this value where the
/// provider supports it.
/// </param>
/// <param name="Metadata">Caller-supplied metadata attached to the whole batch, when any.</param>
public sealed record BaizeBatchSubmission(
    IReadOnlyList<BaizeBatchRequest> Requests,
    string? Id = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
