namespace Penghou.Baize;

/// <summary>
/// Per-strategy diagnostic for a repair run over JSON produced by a model
/// (for example tool-call arguments or structured output). Reported in
/// configuration order, including strategies the pipeline never reached.
/// </summary>
/// <param name="Name">The name of the repair strategy.</param>
/// <param name="Status">The disposition of the strategy for this run.</param>
/// <param name="Repaired">The repaired text produced by the strategy, when any.</param>
/// <param name="Note">A human-readable note about what the strategy did, when any.</param>
public sealed record LlmRepairAttempt(
    string Name,
    LlmRepairStatus Status,
    string? Repaired = null,
    string? Note = null);

/// <summary>
/// The disposition of a configured repair strategy for a single repair run.
/// </summary>
public enum LlmRepairStatus
{
    /// <summary>The strategy was never invoked because an earlier strategy already produced valid JSON.</summary>
    Skipped,

    /// <summary>The strategy ran and declined to modify the input.</summary>
    NotApplicable,

    /// <summary>The strategy ran but produced no usable result.</summary>
    Failed,

    /// <summary>The strategy produced a repaired candidate.</summary>
    Succeeded
}
