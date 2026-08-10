namespace Penghou.Baize.Batch;

/// <summary>A physical batch part paired with its latest provider status.</summary>
public sealed record ProviderBatchPartStatus(
    ProviderBatchPart Part,
    ProviderBatchStatus Status);
