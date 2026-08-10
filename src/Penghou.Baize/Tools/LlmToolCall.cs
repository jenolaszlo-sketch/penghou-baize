namespace Penghou.Baize;

/// <summary>A tool call the model produced.</summary>
/// <param name="Id">A unique identifier for the call.</param>
/// <param name="Name">The name of the tool being called.</param>
/// <param name="ArgumentsJson">The tool call arguments as JSON text.</param>
/// <param name="JsonWasRepaired">Whether the arguments JSON was repaired.</param>
/// <param name="JsonRepairAttempts">The repair attempts made on the arguments, when any.</param>
/// <param name="NormalizationStatus">
/// The outcome of normalizing the call. The normalizer never silently drops
/// calls: an undeclared tool or a missing arguments object is preserved with a
/// status so callers can decide how to handle it.
/// </param>
/// <param name="Continuation">
/// Provider continuation metadata for the call (for example Gemini's thought
/// signature), required to replay it on a later turn.
/// </param>
public sealed record LlmToolCall(
    string Id,
    string Name,
    string ArgumentsJson,
    bool JsonWasRepaired = false,
    IReadOnlyList<LlmRepairAttempt>? JsonRepairAttempts = null,
    LlmToolCallNormalizationStatus NormalizationStatus =
        LlmToolCallNormalizationStatus.Normalized,
    LlmProviderContinuation? Continuation = null)
{
    /// <summary>Detailed diagnostics from arguments JSON repair.</summary>
    public LlmJsonRepairDiagnostics? JsonRepairDiagnostics { get; init; }
}

/// <summary>How a native tool call was left by normalization.</summary>
public enum LlmToolCallNormalizationStatus
{
    /// <summary>
    /// The call names a declared tool and carried arguments; it was
    /// canonicalized, repairing the JSON when needed.
    /// </summary>
    Normalized,

    /// <summary>
    /// The call names a tool that is not declared in the request; it is
    /// preserved as produced so the application can audit or reject it.
    /// </summary>
    UnknownTool,

    /// <summary>
    /// The call names a declared tool but carried no arguments; it is
    /// preserved as produced so the application can handle the missing
    /// arguments explicitly.
    /// </summary>
    EmptyArguments
}
