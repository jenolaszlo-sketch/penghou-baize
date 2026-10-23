using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Penghou.Baize.Tools;

namespace Penghou.Baize.Qualification;

// The original QualificationScenarios remains frozen for historical evidence.
internal static class QualificationScenariosV3
{
    public static Task<QualificationResultV3> RunBaselineAsync(QualificationContextV3 context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public static Task<QualificationResultV3> RunSingleToolAsync(QualificationContextV3 context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    private static async Task<QualificationResultV3> RunAsync(QualificationContextV3 context, bool tools, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Stream);
        var descriptor = tools ? QualificationContractsV3.SingleTool : QualificationContractsV3.Baseline;
        var observed = new Observation();
        string? CancellationFailure() => caller.IsCancellationRequested ? "caller-cancelled" : context.Deadline.IsCancellationRequested ? "timeout" : null;
        var preflight = CancellationFailure()
            ?? (tools && !context.NativeToolCalling ? "native-tools-not-configured"
                : tools && !QualificationContractsV3.KnownProtocol(context.IdentityProtocol) ? "identity-protocol-unspecified"
                : context.OutputBudget == OutputBudgetSupport.Unsupported ? "output-budget-unsupported"
                : context.OutputBudget == OutputBudgetSupport.Insufficient ? "output-budget-insufficient" : null);
        if (preflight is not null)
        {
            var disposition = preflight == "caller-cancelled" ? ContractDisposition.Cancelled : ContractDisposition.Blocked;
            ContractResult Unexecuted(ContractDescriptor contract) => new(contract.Id, disposition,
                Array.AsReadOnly(contract.AssertionIds.Select(id => Missing(id, preflight)).ToArray()));
            return new(Array.AsReadOnly(new[] { Unexecuted(descriptor), Unexecuted(QualificationContractsV3.Streaming) }),
                observed.Diagnostics(context, [], 0, preflight, null), observed.RouterEvidence(), observed.Identity(context, null));
        }

        LlmResponse? response = null;
        string? failure = null;
        int? httpStatus = null;
        using var effective = CancellationTokenSource.CreateLinkedTokenSource(caller, context.Deadline);
        using var telemetry = new IntegrityTelemetry();
        try
        {
            var request = tools ? QualificationFixtures.CreateSingleToolRequest() : QualificationFixtures.CreateBaselineRequest();
            response = await observed.ObserveAsync(context.Stream(request, effective.Token), effective.Token)
                .CollectAsync(cancellationToken: effective.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { failure = CancellationFailure() ?? "ambiguous-cancellation"; }
        catch (TimeoutException) { failure = CancellationFailure() ?? "timeout"; }
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
        catch (LlmRequestValidationException) { failure = "request-validation"; }
        catch (Exception) { failure = "execution-error"; }

        failure = CancellationFailure() ?? failure;
        var integrity = telemetry.Snapshot();
        var checks = tools ? await ToolAssertionsAsync(observed, context, response, effective.Token).ConfigureAwait(false)
            : new List<AssertionResult>
            {
                Final("response.usable", !string.IsNullOrWhiteSpace(observed.Content.ToString()), "empty-content"),
                Final("response.expected-value", observed.Content.ToString().Trim() == QualificationFixtures.ExpectedBaseline, "unexpected-value"),
                Absence("response.shape", observed.Tools.Count > 0, "undeclared-tool-call"),
                Absence("response.unrepaired", observed.RepairPathObserved, "repair-path-observed")
            };
        failure = CancellationFailure() ?? failure;
        var finish = LlmFinishReasonClassifier.Classify(observed.FinishReason);
        checks.Add(Final("response.finish", finish == LlmFinishReasonKind.Stop || tools && finish == LlmFinishReasonKind.ToolCall,
            finish == LlmFinishReasonKind.LengthLimit ? "output-budget-exhausted" : "unsuccessful-finish"));
        checks.Add(Check("execution.completed", failure is null && observed.Completed && observed.TerminalCount == 1 &&
            !observed.PayloadAfterTerminal && observed.ProviderDone != false && observed.RouterValid(context) &&
            integrity.Count <= 1 && integrity.All(s => s.Succeeded != false && s.WarningCount is not > 0), failure ?? "incomplete-or-unsuccessful-stream"));
        checks.Add(failure is not null && observed.RouterSnapshots <= 1
            ? Missing("diagnostics.router", "execution-incomplete")
            : Check("diagnostics.router", observed.RouterValid(context), "router-diagnostics-inconsistent"));

        var forced = failure switch
        {
            "caller-cancelled" => ContractDisposition.Cancelled,
            "ambiguous-cancellation" or "execution-error" => ContractDisposition.Inconclusive,
            "timeout" or "provider-unavailable" or "rate-limited" or "authentication" or "authorization" or
                "request-validation" or "invalid-request" => ContractDisposition.Blocked,
            _ => (ContractDisposition?)null
        };
        return new(Array.AsReadOnly(new[] { Result(descriptor, checks, forced),
            Result(QualificationContractsV3.Streaming, StreamingAssertions(observed, context, response, integrity, failure), forced) }),
            observed.Diagnostics(context, integrity, 1, failure, httpStatus), observed.RouterEvidence(), observed.Identity(context, response));

        AssertionResult Final(string id, bool pass, string code) => observed.Completed && response is not null
            ? Check(id, pass, code) : Missing(id, "execution-incomplete");
        AssertionResult Absence(string id, bool violation, string code) => violation ? Check(id, false, code)
            : observed.Completed ? Check(id, true, code) : Missing(id, "execution-incomplete");
    }

    private static async Task<List<AssertionResult>> ToolAssertionsAsync(Observation observed, QualificationContextV3 context,
        LlmResponse? response, CancellationToken token)
    {
        var single = observed.Tools.Count == 1 ? observed.Tools.Values.Single() : null;
        var arguments = single?.Arguments.ToString();
        var json = ParseJson(arguments);
        var valueCorrect = false;
        if (json.Valid && json.Unique)
        {
            using var document = JsonDocument.Parse(arguments!);
            valueCorrect = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("value", out var value) &&
                value.ValueKind == JsonValueKind.String && value.GetString() == QualificationFixtures.ExpectedToolValue;
        }
        LlmToolArgumentValidationResult? validation = null;
        if (json.Valid && json.Unique && !token.IsCancellationRequested)
        {
            try
            {
                validation = await new StructuralToolArgumentValidator().ValidateAsync(QualificationFixtures.CreateTool(), arguments!, token).ConfigureAwait(false);
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
        AssertionResult Final(string id, bool pass, string code) => observed.Completed && response is not null
            ? Check(id, pass, code) : Missing(id, "execution-incomplete");
        AssertionResult Absence(string id, bool violation, string code) => violation ? Check(id, false, code)
            : observed.Completed ? Check(id, true, code) : Missing(id, "execution-incomplete");
        return
        [
            Final("tool.count", single is not null, "unexpected-tool-count"),
            Final("tool.name", single?.Name == QualificationFixtures.ToolName, "unexpected-tool-name"),
            Final("tool.identity", single is not null && observed.Correlation(context, response), "invalid-tool-correlation"),
            Final("tool.json", json.Valid, "invalid-json"),
            !json.Valid ? Missing("tool.json-unique", "invalid-json") : Final("tool.json-unique", json.Unique, "duplicate-json-property"),
            !json.Valid || !json.Unique ? Missing("tool.schema", "unambiguous-json-required")
                : token.IsCancellationRequested && validation is null ? Missing("tool.schema", "validation-incomplete")
                : Final("tool.schema", validation is { IsValid: true } && validation.UnsupportedKeywords is not { Count: > 0 }, schemaCode),
            !json.Valid || !json.Unique ? Missing("tool.value", "unambiguous-json-required") : Final("tool.value", valueCorrect, "unexpected-value"),
            Absence("tool.unrepaired", observed.RepairPathObserved, "repair-path-observed"),
            Absence("response.shape", !string.IsNullOrWhiteSpace(observed.Content.ToString()), "unexpected-answer-content")
        ];
    }

    private static List<AssertionResult> StreamingAssertions(Observation observed, QualificationContextV3 context, LlmResponse? response,
        IReadOnlyList<IntegrityObservation> integrity, string? failure)
    {
        var toolsConsistent = !observed.InvalidToolIndex && observed.Tools.Values.All(t =>
            !t.IdentityConflict && !t.PartConflict && !string.IsNullOrWhiteSpace(t.Name) &&
            (t.Id is null ? !QualificationContractsV3.RequiresStreamId(context.IdentityProtocol) && QualificationContractsV3.KnownProtocol(context.IdentityProtocol) : t.Id.Length > 0) &&
            ParseJson(t.Arguments.ToString()) is (true, true));
        var reconstruction = response is not null && response.Content == observed.Content.ToString() &&
            (response.Reasoning ?? "") == observed.Reasoning.ToString() && observed.Correlation(context, response);
        var snapshot = integrity.Count == 1 ? integrity[0] : null;
        var emitted = observed.Content.Length + observed.Reasoning.Length + observed.Tools.Values.Sum(t => t.Arguments.Length);
        return
        [
            !observed.Completed && !observed.PayloadAfterTerminal && !observed.InvalidPartIndex && !observed.Tools.Values.Any(t => t.PartConflict)
                ? Missing("stream.sequence", "execution-incomplete")
                : Check("stream.sequence", !observed.PayloadAfterTerminal && !observed.InvalidPartIndex && !observed.Tools.Values.Any(t => t.PartConflict), "invalid-event-sequence"),
            failure is not null && observed.TerminalCount == 0 ? Missing("stream.terminal", "execution-incomplete")
                : Check("stream.terminal", observed.TerminalCount == 1 && observed.Completed &&
                    LlmFinishReasonClassifier.Classify(observed.FinishReason) is LlmFinishReasonKind.Stop or LlmFinishReasonKind.ToolCall && observed.ProviderDone != false, "incomplete-terminal"),
            response is null ? Missing("stream.reconstruction", "execution-incomplete") : Check("stream.reconstruction", reconstruction, "reconstruction-mismatch"),
            !observed.Completed && !observed.InvalidToolIndex && !observed.Tools.Values.Any(t => t.IdentityConflict || t.PartConflict) ? Missing("stream.tool-deltas", "execution-incomplete")
                : Check("stream.tool-deltas", toolsConsistent, "incomplete-or-conflicting-tool-deltas"),
            failure is not null || !observed.Completed ? Check("stream.completed", false, failure ?? "stream-not-completed")
                : Observed("stream.completed", snapshot?.Succeeded is not null, snapshot?.Succeeded == true, "stream-not-completed"),
            Observed("stream.accounting", snapshot?.NormalizedCharacters is not null && snapshot.EmittedCharacters is not null && snapshot.ConsumedCharacters is not null && snapshot.BufferedCharacters is not null && snapshot.ToolCallCount is not null,
                snapshot is not null && (long?)snapshot.NormalizedCharacters == (long?)snapshot.EmittedCharacters + snapshot.ConsumedCharacters + snapshot.BufferedCharacters && snapshot.EmittedCharacters == emitted && snapshot.ToolCallCount == observed.Tools.Count, "character-accounting-mismatch"),
            Observed("stream.buffer", snapshot?.BufferedCharacters is not null, snapshot?.BufferedCharacters == 0, "buffer-not-empty"),
            Observed("stream.warnings", snapshot?.WarningCount is not null, snapshot?.WarningCount == 0, "protocol-warning"),
            integrity.Count > 1 || observed.RouterSnapshots > 1 || observed.RouterAttempts is > 1
                ? Check("stream.dispatches", false, "multiple-or-conflicting-dispatches")
                : failure is not null ? Missing("stream.dispatches", "execution-incomplete")
                : Observed("stream.dispatches", integrity.Count == 1, observed.RouterValid(context), "router-diagnostics-inconsistent")
        ];
    }

    private static (bool Valid, bool Unique) ParseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (false, false);
        try
        {
            using var document = JsonDocument.Parse(value);
            return (true, Unique(document.RootElement));
        }
        catch (JsonException) { return (false, false); }

        static bool Unique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                    if (!names.Add(property.Name) || !Unique(property.Value)) return false;
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) if (!Unique(item)) return false;
            return true;
        }
    }

    private static AssertionResult Check(string id, bool passed, string code) => new(id, passed ? AssertionStatus.Passed : AssertionStatus.Failed, passed ? "ok" : code);
    private static AssertionResult Missing(string id, string code) => new(id, AssertionStatus.NotObserved, code);
    private static AssertionResult Observed(string id, bool available, bool passed, string code) => available ? Check(id, passed, code) : Missing(id, "integrity-telemetry-unavailable");
    private static ContractResult Result(ContractDescriptor descriptor, List<AssertionResult> checks, ContractDisposition? forced) =>
        new(descriptor.Id, forced ?? (checks.Any(c => c.Status == AssertionStatus.Failed) ? ContractDisposition.Failed
            : checks.Any(c => c.Status == AssertionStatus.NotObserved) ? ContractDisposition.Inconclusive : ContractDisposition.Passed), checks.AsReadOnly());

    private sealed class ToolObservation
    {
        public string? Id, Name;
        public int? PartIndex;
        public bool IdentityConflict, PartConflict;
        public readonly StringBuilder Arguments = new();
    }

    private sealed class Observation
    {
        public readonly StringBuilder Content = new(), Reasoning = new();
        public readonly Dictionary<int, ToolObservation> Tools = [];
        private readonly List<RouterSnapshotEvidence> _routerSnapshots = [];
        public int EventCount, TerminalCount, RouterSnapshots;
        public int? RouterAttempts, RouterSucceeded, RouterFailed;
        public string? FinishReason;
        public bool Completed, PayloadAfterTerminal, InvalidToolIndex, InvalidPartIndex, RepairPathObserved, HasProviderDiagnostics, RouterBeforeTerminal;
        public bool? ProviderDone;
        public LlmUsage? Usage;

        public bool RouterValid(QualificationContextV3 context) => RouterSnapshots == 0 ? !context.RequireRouterDiagnostics
            : RouterSnapshots == 1 && RouterAttempts == 1 && RouterSucceeded == 1 && RouterFailed == 0 && !RouterBeforeTerminal;

        public RouterEvidence RouterEvidence() => new(RouterSnapshots, RouterAttempts, RouterSucceeded, RouterFailed,
            RouterSnapshots > 0 && !RouterBeforeTerminal, _routerSnapshots.AsReadOnly());

        public bool Correlation(QualificationContextV3 context, LlmResponse? response) => response is not null &&
            !InvalidToolIndex && (response.ToolCalls?.Count ?? 0) == Tools.Count &&
            Tools.OrderBy(p => p.Key).Select(p => p.Value).Zip(response.ToolCalls ?? []).All(pair =>
                !pair.First.IdentityConflict && !pair.First.PartConflict && !string.IsNullOrEmpty(pair.Second.Id) &&
                (pair.First.Id is null ? QualificationContractsV3.KnownProtocol(context.IdentityProtocol) && !QualificationContractsV3.RequiresStreamId(context.IdentityProtocol)
                    : pair.First.Id.Length > 0 && pair.First.Id == pair.Second.Id) &&
                pair.First.Name == pair.Second.Name && pair.First.Arguments.ToString() == pair.Second.ArgumentsJson) &&
            (response.ToolCalls ?? []).Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == Tools.Count;

        public ToolIdentityEvidence Identity(QualificationContextV3 context, LlmResponse? response)
        {
            var applicability = context.IdentityProtocol == ToolIdentityProtocol.OllamaChat ? IdentityApplicability.NotApplicable
                : QualificationContractsV3.KnownProtocol(context.IdentityProtocol) ? IdentityApplicability.Applicable : IdentityApplicability.Undetermined;
            var correlation = Tools.Count == 0 || response is null ? AssertionStatus.NotObserved : Correlation(context, response) ? AssertionStatus.Passed : AssertionStatus.Failed;
            var origin = correlation != AssertionStatus.Passed ? CanonicalIdentityOrigin.NotObserved
                : Tools.Values.Any(t => t.Id is null) ? CanonicalIdentityOrigin.CollectorAssigned : CanonicalIdentityOrigin.NormalizedStream;
            return new(context.IdentityProtocol, correlation, origin, applicability, AssertionStatus.NotObserved,
                applicability == IdentityApplicability.NotApplicable ? "protocol-has-no-wire-call-id" : "wire-provenance-not-observed");
        }

        public async IAsyncEnumerable<LlmStreamEvent> ObserveAsync(IAsyncEnumerable<LlmStreamEvent> stream, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var evt in stream.WithCancellation(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                EventCount++;
                PayloadAfterTerminal |= TerminalCount > 0 && (evt.Delta is { Length: > 0 } || evt.ReasoningContent is { Length: > 0 } || evt.ToolCallDelta is not null);
                InvalidPartIndex |= evt.PartIndex < 0;
                Content.Append(evt.Delta);
                Reasoning.Append(evt.ReasoningContent);
                if (evt.ToolCallDelta is { } delta)
                {
                    InvalidToolIndex |= delta.Index < 0;
                    if (!Tools.TryGetValue(delta.Index, out var tool)) Tools.Add(delta.Index, tool = new());
                    tool.IdentityConflict |= delta.Id is not null && tool.Id is not null && delta.Id != tool.Id || delta.Name is not null && tool.Name is not null && delta.Name != tool.Name;
                    tool.PartConflict |= evt.PartIndex.HasValue && tool.PartIndex.HasValue && evt.PartIndex != tool.PartIndex;
                    tool.Id = delta.Id ?? tool.Id;
                    tool.Name = delta.Name ?? tool.Name;
                    tool.PartIndex = evt.PartIndex ?? tool.PartIndex;
                    tool.Arguments.Append(delta.ArgumentsJsonFragment);
                }
                if (evt.FinishReason is not null) { TerminalCount++; FinishReason = evt.FinishReason; }
                if (evt.Usage is not null) Usage = evt.Usage;
                if (evt.RouterDiagnostics is { } router)
                {
                    RouterSnapshots++;
                    RouterBeforeTerminal |= TerminalCount != 1;
                    RouterAttempts = router.Attempts.Count;
                    RouterSucceeded = router.Attempts.Count(a => a.Outcome == LlmRouterAttemptOutcome.Succeeded);
                    RouterFailed = router.Attempts.Count(a => a.Outcome == LlmRouterAttemptOutcome.Failed);
                    _routerSnapshots.Add(new(RouterAttempts.Value, RouterSucceeded.Value, RouterFailed.Value, TerminalCount == 1));
                }
                if (evt.Diagnostics is not null) { HasProviderDiagnostics = true; ProviderDone = evt.Diagnostics.Done; }
                // Native qualification requires bypassing the repair path entirely.
                // This reports path evidence, not a claim that repair changed bytes.
                RepairPathObserved |= evt.ContentWasRepaired || evt.ContentRepairAttempts is { Count: > 0 } || evt.ContentRepairDiagnostics is not null;
                yield return evt;
            }
            Completed = true;
        }

        public QualificationDiagnostics Diagnostics(QualificationContextV3 context, IReadOnlyList<IntegrityObservation> integrity, int invocations, string? failure, int? status) =>
            new(invocations, EventCount, Content.Length, Reasoning.Length, Tools.Values.Sum(t => t.Arguments.Length), TerminalCount,
                LlmFinishReasonClassifier.Classify(FinishReason), RouterAttempts, HasProviderDiagnostics, Usage?.PromptTokens, Usage?.CompletionTokens, Usage?.ThinkingTokens,
                failure, status, context.OutputBudget, integrity);
    }
}
