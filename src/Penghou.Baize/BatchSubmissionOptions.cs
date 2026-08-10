namespace Penghou.Baize;

/// <summary>
/// Options controlling the submission of one physical provider batch.
/// </summary>
public sealed record BatchSubmissionOptions
{
    /// <summary>
    /// A client-supplied idempotency key the provider uses to deduplicate
    /// retried submissions. When set, a retried submission returns the existing
    /// batch instead of creating a duplicate.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Provider-specific submission metadata, interpreted by the provider batch
    /// client (for example OpenAI batch <c>metadata</c>).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
