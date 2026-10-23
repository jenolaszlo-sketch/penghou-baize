using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Penghou.Baize.Tools;

namespace Penghou.Baize.Qualification;

internal static class QualificationScenarios
{
    public static Task<QualificationResult> RunBaselineAsync(
        QualificationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, tools: false, cancellationToken);

    public static Task<QualificationResult> RunSingleToolAsync(
        QualificationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, tools: true, cancellationToken);

    private static async Task<QualificationResult> RunAsync(
        QualificationContext context, bool tools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Stream);
        var descriptor = tools ? QualificationContracts.SingleTool : QualificationContracts.Baseline;
        var observed = new StreamObservation();
        var preflight = cancellationToken.IsCancellationRequested ? "caller-cancelled"
            : tools && !context.NativeToolCalling ? "native-tools-not-configured"
            : context.OutputBudget == OutputBudgetSupport.Unsupported ? "output-budget-unsupported"
            : context.OutputBudget == OutputBudgetSupport.Insufficient ? "output-budget-insufficient"
            : null;
        if (preflight is not null)
        {
            var disposition = cancellationToken.IsCancellationRequested
                ? ContractDisposition.Cancelled : ContractDisposition.Blocked;
            return new(Array.AsReadOnly(new[] { Unexecuted(descriptor), Unexecuted(QualificationContracts.Streaming) }),
                observed.Diagnostics(context, [], 0, preflight, null));

            ContractResult Unexecuted(ContractDescriptor contract) => new(contract.Id, disposition,
                Array.AsReadOnly(contract.AssertionIds.Select(id =>
                    new AssertionResult(id, AssertionStatus.NotObserved, preflight)).ToArray()));
        }

        LlmResponse? response = null;
        string? failure = null;
        int? httpStatus = null;
        using var telemetry = new IntegrityTelemetry();
        try
        {
            var request = tools ? QualificationFixtures.CreateSingleToolRequest() : QualificationFixtures.CreateBaselineRequest();
            response = await observed.ObserveAsync(context.Stream(request, cancellationToken), cancellationToken)
                .CollectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            failure = cancellationToken.IsCancellationRequested ? "caller-cancelled" : "timeout";
        }
        catch (LlmClientException ex)
        {
            failure = ex.FailureKind switch
            {
                LlmClientFailureKind.Availability => "provider-unavailable",
                LlmClientFailureKind.RateLimit => "rate-limited",
                LlmClientFailureKind.Authentication => "authentication",
                LlmClientFailureKind.Authorization => "authorization",
                LlmClientFailureKind.InvalidRequest => "invalid-request",
                LlmClientFailureKind.Content => "content-rejected",
                _ => "protocol-error"
            };
            httpStatus = ex.StatusCode;
        }
        catch (LlmRequestValidationException)
        {
            failure = "request-validation";
        }
        catch (Exception)
        {
            // No exception messages, type names, inner exceptions or stacks in results.
            failure = "execution-error";
        }

        if (cancellationToken.IsCancellationRequested)
            failure = "caller-cancelled";
        var integrity = telemetry.Snapshot();
        var checks = tools
            ? await ToolAssertionsAsync(observed, cancellationToken).ConfigureAwait(false)
            : new List<AssertionResult>
            {
                Check("response.usable", !string.IsNullOrWhiteSpace(observed.Content.ToString()), "empty-content"),
                Check("response.expected-value", observed.Content.ToString().Trim() == QualificationFixtures.ExpectedBaseline, "unexpected-value")
            };
        if (cancellationToken.IsCancellationRequested)
            failure = "caller-cancelled";
        var finish = LlmFinishReasonClassifier.Classify(observed.FinishReason);
        checks.Add(Check("response.finish", finish == LlmFinishReasonKind.Stop || tools && finish == LlmFinishReasonKind.ToolCall,
            finish == LlmFinishReasonKind.LengthLimit ? "output-budget-exhausted" : "unsuccessful-finish"));
        checks.Add(Check("execution.completed", failure is null && observed.Completed && observed.TerminalCount == 1 &&
            !observed.PayloadAfterTerminal && observed.ProviderDone != false &&
            integrity.All(s => s.Succeeded != false && s.WarningCount is not > 0),
            failure ?? "incomplete-or-unsuccessful-stream"));
        checks.Add(Check("diagnostics.router", !context.RequireRouterDiagnostics || observed.RouterAttempts.HasValue, "router-diagnostics-missing"));

        var streamChecks = StreamingAssertions(observed, response, integrity, failure);
        var dispositionOverride = failure switch
        {
            "caller-cancelled" => ContractDisposition.Cancelled,
            "timeout" or "provider-unavailable" or "rate-limited" or "authentication" or "authorization" or "request-validation" => ContractDisposition.Blocked,
            _ => (ContractDisposition?)null
        };
        return new(Array.AsReadOnly(new[]
        {
            Result(descriptor, checks, dispositionOverride),
            Result(QualificationContracts.Streaming, streamChecks, dispositionOverride)
        }), observed.Diagnostics(context, integrity, 1, failure, httpStatus));
    }

    private static async Task<List<AssertionResult>> ToolAssertionsAsync(StreamObservation observed, CancellationToken token)
    {
        var single = observed.Tools.Count == 1 ? observed.Tools.Values.Single() : null;
        var arguments = single?.Arguments.ToString();
        bool jsonValid = false, valueCorrect = false;
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            try
            {
                using var document = JsonDocument.Parse(arguments);
                jsonValid = true;
                valueCorrect = document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("value", out var value) &&
                    value.ValueKind == JsonValueKind.String && value.GetString() == QualificationFixtures.ExpectedToolValue;
            }
            catch (JsonException) { }
        }

        // Validate the original arguments; do not normalize or repair to manufacture
        // native success. This schema uses only the validator's supported keywords.
        LlmToolArgumentValidationResult? validation = null;
        if (jsonValid && !token.IsCancellationRequested)
        {
            try
            {
                validation = await new StructuralToolArgumentValidator()
                    .ValidateAsync(QualificationFixtures.CreateTool(), arguments!, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        var schemaCode = validation?.Failures.FirstOrDefault()?.Code switch
        {
            "required-missing" => "required-missing",
            "type-mismatch" => "type-mismatch",
            "unknown-property" => "unknown-property",
            _ => "schema-invalid"
        };
        // Do not expose paths/messages: even an unexpected property name can be private.
        return
        [
            Check("tool.count", single is not null, "unexpected-tool-count"),
            Check("tool.name", single?.Name == QualificationFixtures.ToolName, "unexpected-tool-name"),
            Check("tool.identity", single is not null && ValidIdentity(single.Id) && !single.IdentityConflict && !observed.InvalidToolIndex, "invalid-tool-identity"),
            Check("tool.json", jsonValid, "invalid-json"),
            Check("tool.schema", validation is { IsValid: true } && validation.UnsupportedKeywords is not { Count: > 0 }, schemaCode),
            Check("tool.value", valueCorrect, "unexpected-value"),
            Check("tool.unrepaired", !observed.RepairObserved, "repair-observed")
        ];
    }

    private static List<AssertionResult> StreamingAssertions(StreamObservation observed, LlmResponse? response,
        IReadOnlyList<IntegrityObservation> integrity, string? failure)
    {
        var toolsConsistent = !observed.InvalidToolIndex && observed.Tools.Values.All(t =>
            !t.IdentityConflict && !string.IsNullOrWhiteSpace(t.Name) && ValidIdentity(t.Id) && IsJson(t.Arguments.ToString()));
        var reconstruction = response is not null &&
            string.Equals(response.Content, observed.Content.ToString(), StringComparison.Ordinal) &&
            string.Equals(response.Reasoning ?? "", observed.Reasoning.ToString(), StringComparison.Ordinal) &&
            (response.ToolCalls?.Count ?? 0) == observed.Tools.Count &&
            observed.Tools.OrderBy(p => p.Key).Select(p => p.Value).Zip(response.ToolCalls ?? []).All(pair =>
                pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name &&
                pair.First.Arguments.ToString() == pair.Second.ArgumentsJson);
        var snapshot = integrity.Count == 1 ? integrity[0] : null;
        var emitted = observed.Content.Length + observed.Reasoning.Length + observed.Tools.Values.Sum(t => t.Arguments.Length);
        return
        [
            Check("stream.sequence", !observed.PayloadAfterTerminal && !observed.InvalidPartIndex, "invalid-event-sequence"),
            Check("stream.terminal", observed.TerminalCount == 1 && observed.Completed &&
                LlmFinishReasonClassifier.Classify(observed.FinishReason) is LlmFinishReasonKind.Stop or LlmFinishReasonKind.ToolCall &&
                observed.ProviderDone != false, "incomplete-terminal"),
            Check("stream.reconstruction", reconstruction, "reconstruction-mismatch"),
            Check("stream.tool-deltas", toolsConsistent, "incomplete-or-conflicting-tool-deltas"),
            failure is not null || !observed.Completed
                ? Check("stream.completed", false, failure ?? "stream-not-completed")
                : Observed("stream.completed", snapshot?.Succeeded is not null, snapshot?.Succeeded == true, "stream-not-completed"),
            Observed("stream.accounting", snapshot?.NormalizedCharacters is not null && snapshot.EmittedCharacters is not null && snapshot.ConsumedCharacters is not null && snapshot.BufferedCharacters is not null && snapshot.ToolCallCount is not null,
                snapshot is not null && (long?)snapshot.NormalizedCharacters == (long?)snapshot.EmittedCharacters + snapshot.ConsumedCharacters + snapshot.BufferedCharacters && snapshot.EmittedCharacters == emitted && snapshot.ToolCallCount == observed.Tools.Count, "character-accounting-mismatch"),
            Observed("stream.buffer", snapshot?.BufferedCharacters is not null, snapshot?.BufferedCharacters == 0, "buffer-not-empty"),
            Observed("stream.warnings", snapshot?.WarningCount is not null, snapshot?.WarningCount == 0, "protocol-warning"),
            Check("stream.dispatches", integrity.Count <= 1 && observed.RouterAttempts is not > 1, "multiple-dispatches-observed")
        ];
    }

    private static bool ValidIdentity(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));

    private static bool IsJson(string value)
    {
        try { using var document = JsonDocument.Parse(value); return true; }
        catch (JsonException) { return false; }
    }

    private static AssertionResult Check(string id, bool passed, string code) =>
        new(id, passed ? AssertionStatus.Passed : AssertionStatus.Failed, passed ? "ok" : code);

    private static AssertionResult Observed(string id, bool available, bool passed, string code) =>
        available ? Check(id, passed, code) : new(id, AssertionStatus.NotObserved, "integrity-telemetry-unavailable");

    private static ContractResult Result(ContractDescriptor descriptor, List<AssertionResult> checks, ContractDisposition? forced) =>
        new(descriptor.Id, forced ?? (checks.Any(c => c.Status == AssertionStatus.Failed) ? ContractDisposition.Failed
            : checks.Any(c => c.Status == AssertionStatus.NotObserved) ? ContractDisposition.Inconclusive : ContractDisposition.Passed),
            checks.AsReadOnly());

    private sealed class ToolObservation
    {
        public string? Id;
        public string? Name;
        public readonly StringBuilder Arguments = new();
        public bool IdentityConflict;
    }

    private sealed class StreamObservation
    {
        public readonly StringBuilder Content = new();
        public readonly StringBuilder Reasoning = new();
        public readonly Dictionary<int, ToolObservation> Tools = [];
        public int EventCount, TerminalCount;
        public string? FinishReason;
        public bool Completed, PayloadAfterTerminal, InvalidToolIndex, InvalidPartIndex, RepairObserved, HasProviderDiagnostics;
        public bool? ProviderDone;
        public int? RouterAttempts;
        public LlmUsage? Usage;

        public async IAsyncEnumerable<LlmStreamEvent> ObserveAsync(IAsyncEnumerable<LlmStreamEvent> stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var evt in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EventCount++;
                PayloadAfterTerminal |= TerminalCount > 0 && (evt.Delta is { Length: > 0 } || evt.ReasoningContent is { Length: > 0 } || evt.ToolCallDelta is not null);
                InvalidPartIndex |= evt.PartIndex < 0;
                Content.Append(evt.Delta);
                Reasoning.Append(evt.ReasoningContent);
                if (evt.ToolCallDelta is { } delta)
                {
                    InvalidToolIndex |= delta.Index < 0;
                    if (!Tools.TryGetValue(delta.Index, out var tool))
                        Tools.Add(delta.Index, tool = new());
                    tool.IdentityConflict |= delta.Id is not null && tool.Id is not null && delta.Id != tool.Id ||
                        delta.Name is not null && tool.Name is not null && delta.Name != tool.Name;
                    tool.Id = delta.Id ?? tool.Id;
                    tool.Name = delta.Name ?? tool.Name;
                    tool.Arguments.Append(delta.ArgumentsJsonFragment);
                }
                if (evt.FinishReason is not null) { TerminalCount++; FinishReason = evt.FinishReason; }
                if (evt.Usage is not null) Usage = evt.Usage;
                if (evt.RouterDiagnostics is not null) RouterAttempts = Math.Max(RouterAttempts ?? 0, evt.RouterDiagnostics.Attempts.Count);
                if (evt.Diagnostics is not null) { HasProviderDiagnostics = true; ProviderDone = evt.Diagnostics.Done; }
                RepairObserved |= evt.ContentWasRepaired || evt.ContentRepairAttempts is { Count: > 0 };
                yield return evt;
            }
            Completed = true;
        }

        public QualificationDiagnostics Diagnostics(QualificationContext context, IReadOnlyList<IntegrityObservation> integrity,
            int invocations, string? failure, int? status) => new(invocations, EventCount, Content.Length, Reasoning.Length,
                Tools.Values.Sum(t => t.Arguments.Length), TerminalCount, LlmFinishReasonClassifier.Classify(FinishReason),
                RouterAttempts, HasProviderDiagnostics, Usage?.PromptTokens, Usage?.CompletionTokens, Usage?.ThinkingTokens,
                failure, status, context.OutputBudget, integrity);
    }
}
