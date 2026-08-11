namespace Penghou.Baize.Batch;

/// <summary>Identifies one failed physical submission within a logical batch.</summary>
public sealed record BaizeBatchSubmissionFailure(
    int GroupIndex,
    string EndpointId,
    Exception Error);
