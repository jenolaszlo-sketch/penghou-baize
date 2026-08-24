namespace Penghou.Baize;

/// <summary>A single event yielded while streaming a completion.</summary>
/// <param name="Delta">A fragment of content text, when present.</param>
/// <param name="ReasoningContent">A fragment of the model's reasoning or thinking text, when present.</param>
/// <param name="FinishReason">The reason the generation finished, when reported.</param>
/// <param name="Usage">Token usage for the call, when reported.</param>
/// <param name="ToolCallDelta">A fragment of a tool call, when present.</param>
/// <param name="Diagnostics">Provider diagnostics for the call, when reported.</param>
/// <param name="RateLimit">Rate-limit and quota information for the call, when reported.</param>
/// <param name="RouterDiagnostics">Routing diagnostics for a routed stream, when the stream was routed.</param>
/// <param name="Continuation">
/// Provider continuation metadata for the fragment, when the provider requires
/// opaque values (for example Gemini's thought signature) to be replayed on a
/// later turn.
/// </param>
public sealed record LlmStreamEvent(
    string? Delta = null,
    string? ReasoningContent = null,
    string? FinishReason = null,
    LlmUsage? Usage = null,
    ToolCallDelta? ToolCallDelta = null,
    LlmProviderDiagnostics? Diagnostics = null,
    LlmRateLimitInfo? RateLimit = null,
    LlmRouterDiagnostics? RouterDiagnostics = null,
    LlmProviderContinuation? Continuation = null)
{
    /// <summary>
    /// Gets the provider-neutral classification of <see cref="FinishReason"/>.
    /// </summary>
    public LlmFinishReasonKind FinishReasonKind =>
        LlmFinishReasonClassifier.Classify(FinishReason);

    /// <summary>
    /// Gets the provider-assigned index of the response content part this
    /// event updates. Events sharing an index are accumulated into one ordered
    /// part.
    /// </summary>
    public int? PartIndex { get; init; }

    /// <summary>Whether a response decorator repaired structured content.</summary>
    public bool ContentWasRepaired { get; init; }

    /// <summary>The structured-content repair attempts, when any.</summary>
    public IReadOnlyList<LlmRepairAttempt>? ContentRepairAttempts { get; init; }

    /// <summary>Detailed structured-content repair diagnostics.</summary>
    public LlmJsonRepairDiagnostics? ContentRepairDiagnostics { get; init; }
}
