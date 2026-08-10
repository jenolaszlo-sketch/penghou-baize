namespace Penghou.Baize;

/// <summary>Provider-neutral diagnostics for a JSON repair operation.</summary>
public sealed record LlmJsonRepairDiagnostics(
    LlmRepairShapeStatus ShapeStatus,
    IReadOnlyList<string> ShapeErrors,
    string? SucceededBy = null,
    LlmTolerantRecoveryDiagnostics? TolerantRecovery = null);

/// <summary>Whether repaired JSON matched the supplied schema expectation.</summary>
public enum LlmRepairShapeStatus
{
    /// <summary>No shape expectation was evaluated.</summary>
    NotEvaluated,

    /// <summary>The repaired JSON matched the structural expectation.</summary>
    Matched,

    /// <summary>The repaired JSON did not match the structural expectation.</summary>
    Mismatched
}

/// <summary>Diagnostics from tolerant syntax-tree recovery.</summary>
public sealed record LlmTolerantRecoveryDiagnostics(
    bool Succeeded,
    string Outcome,
    int CorrectionCount,
    int SchemaGuidedStringCorrectionCount,
    IReadOnlyList<string> Corrections);
