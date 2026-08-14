namespace Penghou.Baize.Generation;

/// <summary>
/// A logical generation batch: a base request replicated across a total count,
/// split into submission chunks bounded by the endpoint's native
/// <see cref="GenerationCapabilities.MaximumCandidates"/>, and executed with
/// bounded concurrency.
/// </summary>
/// <param name="Request">The base request. For image requests any <c>Count</c> is
/// overridden by <see cref="TotalCount"/>, which is authoritative for the batch.</param>
/// <param name="TotalCount">The total number of candidates/assets the batch should
/// produce across all chunks. Must be at least 1.</param>
/// <param name="MaxConcurrency">The maximum number of chunks submitted in
/// parallel. Must be at least 1.</param>
public sealed record GenerationBatchRequest(
    GenerationRequest Request,
    int TotalCount,
    int MaxConcurrency = 4);