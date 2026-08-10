namespace Penghou.Baize.Batch;

/// <summary>
/// A serializable continuation token for a submitted logical batch. It acts as
/// the sole reference required to poll, retrieve, or cancel the operation from
/// any process; Baize itself does not persist it.
/// </summary>
/// <param name="LogicalBatchId">The stable logical batch identifier.</param>
/// <param name="Parts">The physical provider batches the logical batch was split into.</param>
public sealed record BaizeBatchHandle(
    string LogicalBatchId,
    IReadOnlyList<ProviderBatchPart> Parts)
{
    /// <summary>Whether the handle references at least one physical provider batch.</summary>
    public bool IsEmpty => Parts.Count == 0;
}

/// <summary>
/// One physical provider batch inside a <see cref="BaizeBatchHandle"/>.
/// </summary>
/// <param name="ProviderId">The provider adapter that owns the batch.</param>
/// <param name="BatchId">The provider-assigned batch identifier.</param>
/// <param name="EndpointId">The configured endpoint the batch was submitted through.</param>
/// <param name="RequestIds">The logical request identifiers contained in this physical batch.</param>
/// <param name="Metadata">Provider submission metadata echoed back, when any.</param>
public sealed record ProviderBatchPart(
    string ProviderId,
    string BatchId,
    string EndpointId,
    IReadOnlyList<string> RequestIds,
    IReadOnlyDictionary<string, string>? Metadata = null);
