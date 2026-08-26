namespace Penghou.Baize;

internal enum StreamTerminalKind
{
    EndOfStream,
    DoneSentinel,
    FinishReason,
    MessageStop,
    ProviderDone
}

internal sealed record StreamTerminalSignal(
    StreamTerminalKind Kind,
    bool ProtocolCompleted,
    string? FinishReason = null);

internal sealed record NormalizedStreamDelta(
    LlmStreamEvent? Event,
    int ProviderCharacterCount,
    int ProviderChunkCount = 1)
{
    public int NormalizedCharacterCount =>
        (Event?.Delta?.Length ?? 0) +
        (Event?.ReasoningContent?.Length ?? 0) +
        (Event?.ToolCallDelta?.ArgumentsJsonFragment?.Length ?? 0);
}

internal sealed record StreamProtocolWarning(
    string Code,
    string Message);

internal sealed record StreamIntegritySnapshot(
    int ProviderChunkCount,
    int ProviderCharacterCount,
    int NormalizedCharacterCount,
    int EmittedCharacterCount,
    int ConsumedProtocolCharacterCount,
    int BufferedCharacterCount,
    string? FinishReason,
    int ToolCallCount,
    IReadOnlyList<StreamProtocolWarning> ProtocolWarnings)
{
    public bool IsConserved =>
        NormalizedCharacterCount ==
        EmittedCharacterCount +
        ConsumedProtocolCharacterCount +
        BufferedCharacterCount;
}

internal sealed record StreamAssemblyCompletion(
    IReadOnlyList<LlmStreamEvent> Events,
    StreamIntegritySnapshot Diagnostics,
    LlmClientException? Error);
