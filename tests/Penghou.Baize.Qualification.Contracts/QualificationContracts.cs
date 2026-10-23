namespace Penghou.Baize.Qualification;

internal enum AssertionStatus { Passed, Failed, NotObserved }
internal enum ContractDisposition { Passed, Failed, Inconclusive, Blocked, Cancelled }
internal enum OutputBudgetSupport { Unknown, Supported, Unsupported, Insufficient }

// Assertion identifiers are scoped/versioned by their descriptor, not editable policy.
internal sealed record AssertionResult(string Id, AssertionStatus Status, string Code);

internal sealed record ContractDescriptor(
    string Id,
    string FixtureId,
    bool RequiresNativeTools,
    IReadOnlyList<string> AssertionIds)
{
    public int MaximumDispatches => 1;
    public int MaximumOutputTokens => 512;
}

internal sealed record ContractResult(
    string ContractId,
    ContractDisposition Disposition,
    IReadOnlyList<AssertionResult> Assertions);

// This is an execution seam, not a client factory. The caller owns endpoint binding,
// credentials, admission and the one-dispatch composition (including hidden retries).
internal sealed record QualificationContext(
    Func<LlmRequest, CancellationToken, IAsyncEnumerable<LlmStreamEvent>> Stream,
    bool NativeToolCalling = false,
    bool RequireRouterDiagnostics = false,
    OutputBudgetSupport OutputBudget = OutputBudgetSupport.Unknown);

// Only content-free, typed observations leave the scenario. Never persist raw
// LlmResponse/diagnostics/exception objects: they may contain payloads or identifiers.
internal sealed record QualificationDiagnostics(
    int InvocationCount,
    int EventCount,
    int ContentCharacters,
    int ReasoningCharacters,
    int ToolArgumentCharacters,
    int TerminalCount,
    LlmFinishReasonKind FinishReason,
    int? RouterAttemptCount,
    bool ProviderDiagnosticsObserved,
    int? PromptTokens,
    int? CompletionTokens,
    int? ThinkingTokens,
    string? FailureCode,
    int? HttpStatusCode,
    OutputBudgetSupport OutputBudget,
    IReadOnlyList<IntegrityObservation> Integrity);

internal sealed record QualificationResult(
    IReadOnlyList<ContractResult> Contracts,
    QualificationDiagnostics Diagnostics)
{
    public bool Succeeded => Contracts.All(c => c.Disposition == ContractDisposition.Passed);
}

internal static class QualificationContracts
{
    public static ContractDescriptor Baseline { get; } = new(
        "chat.baseline@2", "baize-token@1", false,
        Array.AsReadOnly(new[] { "response.usable", "response.expected-value", "response.finish", "execution.completed", "diagnostics.router" }));

    public static ContractDescriptor Streaming { get; } = new(
        "chat.streaming-integrity@1", "observed-stream@1", false,
        Array.AsReadOnly(new[] { "stream.sequence", "stream.terminal", "stream.reconstruction", "stream.tool-deltas", "stream.completed", "stream.accounting", "stream.buffer", "stream.warnings", "stream.dispatches" }));

    public static ContractDescriptor SingleTool { get; } = new(
        "tools.single@2", "echo-value@1", true,
        Array.AsReadOnly(new[] { "tool.count", "tool.name", "tool.identity", "tool.json", "tool.schema", "tool.value", "tool.unrepaired", "response.finish", "execution.completed", "diagnostics.router" }));
}
