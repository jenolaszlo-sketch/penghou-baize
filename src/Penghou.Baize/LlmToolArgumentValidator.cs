namespace Penghou.Baize;

/// <summary>One authoritative schema validation failure with a JSON path.</summary>
/// <param name="Path">JSON path of the offending value (<c>$</c> for the root).</param>
/// <param name="Code">Stable machine-readable code (for example <c>required-missing</c>).</param>
/// <param name="Message">Bounded human-readable detail without argument values.</param>
public sealed record LlmToolArgumentValidationFailure(
    string Path,
    string Code,
    string Message);

/// <summary>Authoritative validation outcome for one tool call's arguments.</summary>
/// <param name="IsValid">Whether the arguments satisfy the tool schema.</param>
/// <param name="Failures">Path-aware failures; empty when valid.</param>
/// <param name="ValidatorId">Identity of the validator that produced this outcome.</param>
/// <param name="UnsupportedKeywords">Schema keywords the validator skipped, reported explicitly rather than silently ignored.</param>
public sealed record LlmToolArgumentValidationResult(
    bool IsValid,
    IReadOnlyList<LlmToolArgumentValidationFailure> Failures,
    string ValidatorId,
    IReadOnlyList<string>? UnsupportedKeywords = null);

/// <summary>
/// Provider-neutral boundary for authoritative tool-argument validation. Runs
/// after Nuwa repair and before a call is reported as fully normalized:
/// repair acceptance proves structural recovery, this boundary proves the
/// declared schema was enforced. Applications replace the validator through
/// dependency injection; validators never mutate arguments.
/// </summary>
public interface ILlmToolArgumentValidator
{
    /// <summary>Validates arguments JSON against the tool's input schema.</summary>
    Task<LlmToolArgumentValidationResult> ValidateAsync(
        LlmTool tool,
        string argumentsJson,
        CancellationToken cancellationToken = default);
}
