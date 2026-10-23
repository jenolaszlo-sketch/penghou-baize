namespace Penghou.Baize.Qualification;

// Protocol selection is explicit composition metadata, never inferred from a model
// name or from the spelling of a returned ID. See the remediation decision record.
internal enum ToolIdentityProtocol { Unspecified, OpenAiChatCompletions, AnthropicMessages, OllamaChat, GeminiGenerateContent }
internal enum IdentityApplicability { Applicable, NotApplicable, Undetermined }
internal enum CanonicalIdentityOrigin { NotObserved, NormalizedStream, CollectorAssigned }

internal sealed record ToolIdentityEvidence(
    ToolIdentityProtocol Protocol,
    AssertionStatus CanonicalCorrelation,
    CanonicalIdentityOrigin CanonicalOrigin,
    IdentityApplicability ProviderIdentityApplicability,
    AssertionStatus ProviderIdentityStatus,
    string ProviderIdentityCode);

internal sealed record RouterSnapshotEvidence(int AttemptCount, int SuccessfulAttempts, int FailedAttempts, bool AfterFinish);
internal sealed record RouterEvidence(int SnapshotCount, int? AttemptCount, int? SuccessfulAttempts, int? FailedAttempts,
    bool TerminalOrderValid, IReadOnlyList<RouterSnapshotEvidence> Snapshots);

internal sealed record QualificationContextV3(
    Func<LlmRequest, CancellationToken, IAsyncEnumerable<LlmStreamEvent>> Stream,
    bool NativeToolCalling = false,
    bool RequireRouterDiagnostics = false,
    OutputBudgetSupport OutputBudget = OutputBudgetSupport.Unknown,
    ToolIdentityProtocol IdentityProtocol = ToolIdentityProtocol.Unspecified,
    CancellationToken Deadline = default);

// Provider identity is deliberately separate from native validity/correlation.
// NotObserved provenance cannot become a provider-ID capability pass. Succeeded
// refers only to the two explicitly named scenario/stream contracts below.
internal sealed record QualificationResultV3(
    IReadOnlyList<ContractResult> Contracts,
    QualificationDiagnostics Diagnostics,
    RouterEvidence Router,
    ToolIdentityEvidence Identity)
{
    public bool Succeeded => Contracts.All(c => c.Disposition == ContractDisposition.Passed);
}

internal static class QualificationContractsV3
{
    public static ContractDescriptor Baseline { get; } = new(
        "chat.baseline@3", "baize-token@1", false,
        Array.AsReadOnly(new[] { "response.usable", "response.expected-value", "response.shape", "response.unrepaired", "response.finish", "execution.completed", "diagnostics.router" }));

    public static ContractDescriptor Streaming { get; } = new(
        "chat.streaming-integrity@2", "observed-stream@1", false,
        Array.AsReadOnly(new[] { "stream.sequence", "stream.terminal", "stream.reconstruction", "stream.tool-deltas", "stream.completed", "stream.accounting", "stream.buffer", "stream.warnings", "stream.dispatches" }));

    public static ContractDescriptor SingleTool { get; } = new(
        "tools.single@3", "echo-value@1", true,
        Array.AsReadOnly(new[] { "tool.count", "tool.name", "tool.identity", "tool.json", "tool.json-unique", "tool.schema", "tool.value", "tool.unrepaired", "response.shape", "response.finish", "execution.completed", "diagnostics.router" }));

    public static bool KnownProtocol(ToolIdentityProtocol protocol) => protocol is
        ToolIdentityProtocol.OpenAiChatCompletions or ToolIdentityProtocol.AnthropicMessages or
        ToolIdentityProtocol.OllamaChat or ToolIdentityProtocol.GeminiGenerateContent;

    public static bool RequiresStreamId(ToolIdentityProtocol protocol) => protocol is
        ToolIdentityProtocol.OpenAiChatCompletions or ToolIdentityProtocol.AnthropicMessages;
}
