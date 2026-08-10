namespace Penghou.Baize;

/// <summary>A collected completion response.</summary>
/// <param name="Content">The generated content text.</param>
/// <param name="Reasoning">The model's reasoning text, when present.</param>
/// <param name="FinishReason">The reason generation finished, when reported.</param>
/// <param name="Usage">Token usage for the call, when reported.</param>
/// <param name="ToolCalls">Tool calls produced by the model, when any.</param>
/// <param name="Diagnostics">Provider diagnostics for the call, when reported.</param>
/// <param name="RouterDiagnostics">Routing diagnostics for the routed stream, when routed.</param>
/// <param name="ContentWasRepaired">Whether the content JSON was repaired.</param>
/// <param name="ContentRepairAttempts">The repair attempts made on the content, when any.</param>
/// <param name="ReasoningContinuation">
/// Provider continuation metadata for the reasoning text, when the provider
/// requires an opaque value (for example Gemini's thought signature) to be
/// replayed on a later turn.
/// </param>
/// <param name="ContentContinuation">
/// Provider continuation metadata for the generated content text, when the
/// provider attaches signature-like values to a regular content part.
/// </param>
/// <remarks>
/// The raw ordered content parts the provider stream yielded, each retaining
/// its provider continuation. Repair and normalization layers may update the
/// convenience <see cref="Content"/> or <see cref="ToolCalls"/> projections
/// without modifying these raw parts, because signed parts must be replayed
/// exactly as received.
/// </remarks>
public sealed record LlmResponse(
    string Content,
    string? Reasoning = null,
    string? FinishReason = null,
    LlmUsage? Usage = null,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    LlmProviderDiagnostics? Diagnostics = null,
    LlmRouterDiagnostics? RouterDiagnostics = null,
    bool ContentWasRepaired = false,
    IReadOnlyList<LlmRepairAttempt>? ContentRepairAttempts = null,
    LlmProviderContinuation? ReasoningContinuation = null,
    LlmProviderContinuation? ContentContinuation = null)
{
    /// <summary>
    /// Gets the raw ordered content parts yielded by the provider stream.
    /// </summary>
    public IReadOnlyList<LlmContentPart>? Parts { get; init; }

    /// <summary>Detailed diagnostics from content JSON repair.</summary>
    public LlmJsonRepairDiagnostics? ContentRepairDiagnostics { get; init; }
}
