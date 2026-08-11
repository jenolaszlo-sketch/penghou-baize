namespace Penghou.Baize.Batch;

/// <summary>Controls cross-provider logical batch coordination.</summary>
public sealed record BatchCoordinatorOptions
{
    /// <summary>
    /// Maximum physical provider batches submitted concurrently. Defaults to
    /// four to reduce startup latency without creating an unbounded burst.
    /// </summary>
    public int MaxConcurrentSubmissions { get; init; } = 4;
}
