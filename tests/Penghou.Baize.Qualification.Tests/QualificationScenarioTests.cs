using System.Diagnostics;
using System.Text.Json;
using Penghou.Baize.Qualification;

namespace Penghou.Baize.Qualification.Tests;

public sealed class QualificationScenarioTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BaselineAndIntegrity_ShareOneInvocationAndFixedFixture()
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_"), new(Delta: "OK", FinishReason: "stop"),
            new(Usage: new(10, 3, 13)));
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), Token);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(1, fixture.Calls);
        Assert.Equal(512, fixture.Request!.MaxTokens);
        Assert.Equal(0, fixture.Request.Temperature);
        Assert.Equal(2, result.Contracts.Count);
        Assert.Equal("chat.baseline@2", result.Contracts[0].ContractId);
        Assert.Equal("chat.streaming-integrity@1", result.Contracts[1].ContractId);
        Assert.Equal(3, result.Diagnostics.CompletionTokens);
        Assert.True(fixture.Disposed);
    }

    [Theory]
    [InlineData("length", "output-budget-exhausted")]
    [InlineData("MAX_TOKENS", "output-budget-exhausted")]
    [InlineData("content_filter", "unsuccessful-finish")]
    [InlineData("error", "unsuccessful-finish")]
    [InlineData("unknown", "unsuccessful-finish")]
    [InlineData(null, "unsuccessful-finish")]
    public async Task CorrectTextWithUnsuccessfulFinish_Fails(string? finish, string code)
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: finish));
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), Token);
        AssertCheck(result, "chat.baseline@2", "response.expected-value", AssertionStatus.Passed);
        AssertCheck(result, "chat.baseline@2", "response.finish", AssertionStatus.Failed, code);
        Assert.False(result.Succeeded);
        Assert.Equal(1, fixture.Calls);
        Assert.Equal(512, fixture.Request!.MaxTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("prefix BAIZE_OK suffix")]
    public async Task MissingOrExtraText_FailsExactSemanticCheck(string text)
    {
        var result = await QualificationScenarios.RunBaselineAsync(
            new ScenarioFixture(new LlmStreamEvent(Delta: text, FinishReason: "stop")).Context(), Token);
        AssertCheck(result, "chat.baseline@2", "response.expected-value", AssertionStatus.Failed, "unexpected-value");
    }

    [Fact]
    public async Task TruncatedStream_DoesNotPassOnPartialExpectedText()
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK"))
        { Error = new LlmClientException("private", LlmClientFailureKind.Availability) };
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), Token);
        AssertCheck(result, "chat.baseline@2", "execution.completed", AssertionStatus.Failed, "provider-unavailable");
        AssertCheck(result, "chat.streaming-integrity@1", "stream.terminal", AssertionStatus.Failed);
        Assert.Equal(ContractDisposition.Blocked, result.Contracts[0].Disposition);
        Assert.True(fixture.Disposed);
    }

    [Fact]
    public async Task ErrorAfterSuccessfulFinish_IsNotHidden()
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop"))
        { Error = new LlmClientException("private", LlmClientFailureKind.Protocol) };
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), Token);
        AssertCheck(result, "chat.baseline@2", "execution.completed", AssertionStatus.Failed, "protocol-error");
        AssertCheck(result, "chat.streaming-integrity@1", "stream.completed", AssertionStatus.Failed, "protocol-error");
    }

    [Fact]
    public async Task RawWhitespaceIsPreservedWhileSemanticAssertionTrims()
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "\r\n\tBAI"), new(Delta: "ZE_OK \n"), new(FinishReason: "stop"));
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), Token);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal("\r\n\tBAIZE_OK \n".Length, result.Diagnostics.ContentCharacters);
        AssertCheck(result, "chat.streaming-integrity@1", "stream.reconstruction", AssertionStatus.Passed);
    }

    [Fact]
    public async Task MissingTerminal_DuplicateTerminal_AndPayloadAfterTerminalFailSeparately()
    {
        var missing = await QualificationScenarios.RunBaselineAsync(new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK")).Context(), Token);
        AssertCheck(missing, "chat.streaming-integrity@1", "stream.terminal", AssertionStatus.Failed);
        var duplicate = await QualificationScenarios.RunBaselineAsync(
            new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop"), new(FinishReason: "stop")).Context(), Token);
        AssertCheck(duplicate, "chat.streaming-integrity@1", "stream.terminal", AssertionStatus.Failed);
        var late = await QualificationScenarios.RunBaselineAsync(
            new ScenarioFixture(new LlmStreamEvent(FinishReason: "stop"), new(Delta: "BAIZE_OK")).Context(), Token);
        AssertCheck(late, "chat.streaming-integrity@1", "stream.sequence", AssertionStatus.Failed);
    }

    [Theory]
    [InlineData(1, 0, 0, "stream.buffer", "buffer-not-empty")]
    [InlineData(0, 1, 0, "stream.warnings", "protocol-warning")]
    [InlineData(0, 0, 1, "stream.accounting", "character-accounting-mismatch")]
    public async Task IntegrityTelemetryFailures_AreIndependent(int buffer, int warnings, int offset, string id, string code)
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop"))
        { BufferedCharacters = buffer, WarningCount = warnings, AccountingOffset = offset };
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), Token);
        AssertCheck(result, "chat.streaming-integrity@1", id, AssertionStatus.Failed, code);
    }

    [Fact]
    public async Task MissingIntegrityTelemetry_IsInconclusiveRatherThanAFalsePass()
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop")) { EmitTelemetry = false };
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), Token);
        AssertCheck(result, "chat.streaming-integrity@1", "stream.buffer", AssertionStatus.NotObserved);
        Assert.Equal(ContractDisposition.Inconclusive, result.Contracts[1].Disposition);
    }

    [Fact]
    public async Task ReportedFailedCompletion_AndMultipleDispatchesAreRejected()
    {
        var failed = await QualificationScenarios.RunBaselineAsync(
            new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop")) { Succeeded = false }.Context(), Token);
        AssertCheck(failed, "chat.streaming-integrity@1", "stream.completed", AssertionStatus.Failed);
        AssertCheck(failed, "chat.baseline@2", "execution.completed", AssertionStatus.Failed);
        var unknown = await QualificationScenarios.RunBaselineAsync(
            new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop")) { Succeeded = null }.Context(), Token);
        AssertCheck(unknown, "chat.streaming-integrity@1", "stream.completed", AssertionStatus.NotObserved);
        var repeated = await QualificationScenarios.RunBaselineAsync(
            new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop")) { TelemetryCopies = 2 }.Context(), Token);
        AssertCheck(repeated, "chat.streaming-integrity@1", "stream.dispatches", AssertionStatus.Failed);
    }

    [Fact]
    public async Task ToolFragments_ReconstructOneCallAndValidateCanonicalSchema()
    {
        var fixture = new ScenarioFixture(
            new(ToolCallDelta: new(0, "call_123", "echo_value", "{\"value\":")),
            new(ToolCallDelta: new(0, ArgumentsJsonFragment: "\"baize-live\"}")), new(FinishReason: "tool_calls"));
        var result = await QualificationScenarios.RunSingleToolAsync(fixture.Context(tools: true), Token);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(1, fixture.Calls);
        Assert.Equal(512, fixture.Request!.MaxTokens);
        Assert.Equal(QualificationFixtures.ToolSchema, Assert.Single(fixture.Request.Tools).InputSchemaJson);
    }

    [Theory]
    [InlineData("wrong_tool", "call_1", "{\"value\":\"baize-live\"}", "tool.name", "unexpected-tool-name")]
    [InlineData("echo_value", null, "{\"value\":\"baize-live\"}", "tool.identity", "invalid-tool-identity")]
    [InlineData("echo_value", "", "{\"value\":\"baize-live\"}", "tool.identity", "invalid-tool-identity")]
    [InlineData("echo_value", "bad id", "{\"value\":\"baize-live\"}", "tool.identity", "invalid-tool-identity")]
    [InlineData("echo_value", "bad\nid", "{\"value\":\"baize-live\"}", "tool.identity", "invalid-tool-identity")]
    [InlineData("echo_value", "call_1", "{", "tool.json", "invalid-json")]
    [InlineData("echo_value", "call_1", "", "tool.json", "invalid-json")]
    [InlineData("echo_value", "call_1", "{}", "tool.schema", "required-missing")]
    [InlineData("echo_value", "call_1", "{\"value\":3}", "tool.schema", "type-mismatch")]
    [InlineData("echo_value", "call_1", "{\"value\":\"baize-live\",\"extra\":true}", "tool.schema", "unknown-property")]
    [InlineData("echo_value", "call_1", "{\"value\":\"wrong\"}", "tool.value", "unexpected-value")]
    [InlineData("echo_value", "call_1", "[]", "tool.schema", "type-mismatch")]
    public async Task InvalidTools_ProduceSpecificAssertionResults(string name, string? id, string json, string check, string code)
    {
        var result = await QualificationScenarios.RunSingleToolAsync(
            new ScenarioFixture(new LlmStreamEvent(ToolCallDelta: new(0, id, name, json)), new(FinishReason: "tool_calls")).Context(tools: true), Token);
        AssertCheck(result, "tools.single@2", check, AssertionStatus.Failed, code);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task MissingNameAndConflictingIdentityAreNotRepairedByCollector()
    {
        var missing = await QualificationScenarios.RunSingleToolAsync(new ScenarioFixture(
            new(ToolCallDelta: new(0, "call_1", ArgumentsJsonFragment: "{}")), new(FinishReason: "tool_calls")).Context(true), Token);
        AssertCheck(missing, "chat.streaming-integrity@1", "stream.tool-deltas", AssertionStatus.Failed);
        AssertCheck(missing, "tools.single@2", "execution.completed", AssertionStatus.Failed, "protocol-error");
        var conflict = await QualificationScenarios.RunSingleToolAsync(new ScenarioFixture(
            new(ToolCallDelta: new(0, "call_1", "echo_value", "{\"value\":")),
            new(ToolCallDelta: new(0, "call_2", ArgumentsJsonFragment: "\"baize-live\"}")),
            new(FinishReason: "tool_calls")).Context(true), Token);
        AssertCheck(conflict, "tools.single@2", "tool.identity", AssertionStatus.Failed);
    }

    [Fact]
    public async Task ExtraCallsAndNegativeIndicesFail()
    {
        var extra = await QualificationScenarios.RunSingleToolAsync(new ScenarioFixture(
            new(ToolCallDelta: new(0, "a", "echo_value", "{}")),
            new(ToolCallDelta: new(1, "b", "echo_value", "{}")), new(FinishReason: "tool_calls")).Context(true), Token);
        AssertCheck(extra, "tools.single@2", "tool.count", AssertionStatus.Failed);
        var negative = await QualificationScenarios.RunSingleToolAsync(new ScenarioFixture(
            new(ToolCallDelta: new(-1, "a", "echo_value", "{\"value\":\"baize-live\"}")), new(FinishReason: "tool_calls")).Context(true), Token);
        AssertCheck(negative, "tools.single@2", "tool.identity", AssertionStatus.Failed);
    }

    [Theory]
    [InlineData("Unsupported", "output-budget-unsupported")]
    [InlineData("Insufficient", "output-budget-insufficient")]
    public async Task InapplicableBudget_DoesNotInvoke(string budget, string code)
    {
        var fixture = new ScenarioFixture();
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context() with { OutputBudget = Enum.Parse<OutputBudgetSupport>(budget) }, Token);
        Assert.Equal(0, fixture.Calls);
        Assert.Equal(ContractDisposition.Blocked, result.Contracts[0].Disposition);
        Assert.All(result.Contracts[0].Assertions, a => Assert.Equal(code, a.Code));
    }

    [Fact]
    public async Task ToolsNotConfigured_DoesNotInvoke()
    {
        var fixture = new ScenarioFixture();
        var result = await QualificationScenarios.RunSingleToolAsync(fixture.Context(), Token);
        Assert.Equal(0, fixture.Calls);
        Assert.Equal("native-tools-not-configured", result.Diagnostics.FailureCode);
    }

    [Fact]
    public async Task CancellationBeforeAndDuringEnumerationIsExplicitAndDisposes()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var before = new ScenarioFixture();
        var result = await QualificationScenarios.RunBaselineAsync(before.Context(), cancellation.Token);
        Assert.Equal(0, before.Calls);
        Assert.Equal(ContractDisposition.Cancelled, result.Contracts[0].Disposition);
        using var during = new CancellationTokenSource();
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK")) { BeforeCompletion = during.Cancel };
        result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), during.Token);
        Assert.Equal(ContractDisposition.Cancelled, result.Contracts[0].Disposition);
        Assert.True(fixture.Disposed);
        Assert.Equal("caller-cancelled", result.Diagnostics.FailureCode);
    }

    [Fact]
    public async Task ResultDoesNotRetainPayloadDiagnosticsOrExceptionSecrets()
    {
        const string secret = "PRIVATE_SENTINEL_credential_signed_url";
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: secret, ReasoningContent: secret,
            Diagnostics: new("fixture", ActualModel: secret, ResponseId: secret)),
            new(RouterDiagnostics: new([new(secret, secret, secret, LlmRouterAttemptOutcome.Failed, TimeSpan.Zero, secret)])))
        { Error = new LlmClientException(secret, LlmClientFailureKind.Authentication, 401, innerException: new Exception(secret)) };
        var result = await QualificationScenarios.RunBaselineAsync(fixture.Context(), Token);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(result));
        Assert.Equal(401, result.Diagnostics.HttpStatusCode);
        Assert.Equal("authentication", result.Diagnostics.FailureCode);
        var tool = await QualificationScenarios.RunSingleToolAsync(new ScenarioFixture(
            new(ToolCallDelta: new(0, secret, "echo_value", "{\"value\":\"baize-live\",\"" + secret + "\":true}")),
            new(FinishReason: "stop")).Context(true), Token);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(tool));
        AssertCheck(tool, "tools.single@2", "tool.schema", AssertionStatus.Failed, "unknown-property");
    }

    [Fact]
    public async Task DescriptorAssertionsMatchExecutionAndBudgetBoundaries()
    {
        var baseline = await QualificationScenarios.RunBaselineAsync(
            new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop")).Context(), Token);
        var tool = await QualificationScenarios.RunSingleToolAsync(new ScenarioFixture(
            new(ToolCallDelta: new(0, "call_1", "echo_value", "{\"value\":\"baize-live\"}")),
            new(FinishReason: "stop")).Context(true), Token);
        Assert.True(tool.Succeeded);
        foreach (var descriptor in new[] { QualificationContracts.Baseline, QualificationContracts.Streaming, QualificationContracts.SingleTool })
        {
            Assert.Equal(1, descriptor.MaximumDispatches);
            Assert.Equal(512, descriptor.MaximumOutputTokens);
            var result = baseline.Contracts.Concat(tool.Contracts).First(c => c.ContractId == descriptor.Id);
            Assert.Equal(descriptor.AssertionIds, result.Assertions.Select(a => a.Id));
        }
    }

    [Fact]
    public async Task RepairAndIncompleteProviderDiagnosticsCannotPass()
    {
        var tool = await QualificationScenarios.RunSingleToolAsync(new ScenarioFixture(
            new(ToolCallDelta: new(0, "call_1", "echo_value", "{\"value\":\"baize-live\"}")) { ContentWasRepaired = true },
            new(FinishReason: "tool_calls")).Context(true), Token);
        AssertCheck(tool, "tools.single@2", "tool.unrepaired", AssertionStatus.Failed, "repair-observed");
        var incomplete = await QualificationScenarios.RunBaselineAsync(new ScenarioFixture(
            new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop", Diagnostics: new("fixture", Done: false))).Context(), Token);
        AssertCheck(incomplete, "chat.baseline@2", "execution.completed", AssertionStatus.Failed);
        AssertCheck(incomplete, "chat.streaming-integrity@1", "stream.terminal", AssertionStatus.Failed);
        var noRouter = await QualificationScenarios.RunBaselineAsync(new ScenarioFixture(
            new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop")).Context() with
        { RequireRouterDiagnostics = true }, Token);
        AssertCheck(noRouter, "chat.baseline@2", "diagnostics.router", AssertionStatus.Failed, "router-diagnostics-missing");
    }

    [Fact]
    public async Task ConcurrentExecutionsDoNotMixTelemetryOrAmbientParent()
    {
        using var parent = new Activity("Caller").Start();
        var first = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop"));
        var second = new ScenarioFixture(new LlmStreamEvent(Delta: "wrong", FinishReason: "stop")) { WarningCount = 1 };
        var results = await Task.WhenAll(QualificationScenarios.RunBaselineAsync(first.Context(), Token),
            QualificationScenarios.RunBaselineAsync(second.Context(), Token));
        Assert.True(results[0].Succeeded, JsonSerializer.Serialize(results[0]));
        AssertCheck(results[1], "chat.streaming-integrity@1", "stream.warnings", AssertionStatus.Failed);
        Assert.Single(results[0].Diagnostics.Integrity);
        Assert.Single(results[1].Diagnostics.Integrity);
        Assert.Same(parent, Activity.Current);
    }

    internal static void AssertCheck(QualificationResult result, string contract, string id, AssertionStatus status, string? code = null)
    {
        var check = Assert.Single(Assert.Single(result.Contracts, c => c.ContractId == contract).Assertions, a => a.Id == id);
        Assert.Equal(status, check.Status);
        if (code is not null) Assert.Equal(code, check.Code);
    }
}
