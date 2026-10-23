using System.Text.Json;
using Penghou.Baize.Qualification;

namespace Penghou.Baize.Qualification.Tests;

public sealed class QualificationV3RegressionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("length")]
    [InlineData("MAX_TOKENS")]
    [InlineData("content_filter")]
    [InlineData("error")]
    [InlineData("unknown")]
    [InlineData(null)]
    public async Task CorrectTextCannotHideUnsuccessfulFinish(string? finish)
    {
        var result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(
            new(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: finish))), Token);
        Assert.False(result.Succeeded);
        QualificationScenarioV3Tests.Check(result, "chat.baseline@3", "response.expected-value", AssertionStatus.Passed);
        QualificationScenarioV3Tests.Check(result, "chat.baseline@3", "response.finish", AssertionStatus.Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("prefix BAIZE_OK suffix")]
    public async Task BaselineStillRequiresExactTrimmedValue(string text)
    {
        var result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(
            new(new LlmStreamEvent(Delta: text, FinishReason: "stop"))), Token);
        QualificationScenarioV3Tests.Check(result, "chat.baseline@3", "response.expected-value", AssertionStatus.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TerminalOrderAndPartIndicesRemainStrict(int shape)
    {
        LlmStreamEvent[] events = shape switch
        {
            0 => [new(Delta: "BAIZE_OK")],
            1 => [new(Delta: "BAIZE_OK", FinishReason: "stop"), new(FinishReason: "stop")],
            2 => [new(FinishReason: "stop"), new(Delta: "BAIZE_OK")],
            _ => [new(Delta: "BAIZE_OK", FinishReason: "stop") { PartIndex = -1 }]
        };
        var result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(new(events)), Token);
        Assert.False(result.Succeeded);
        QualificationScenarioV3Tests.Check(result, "chat.streaming-integrity@2", shape < 2 ? "stream.terminal" : "stream.sequence", AssertionStatus.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AccountingCompletionBufferAndWarningFailuresRemainObservable(int shape)
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop"))
        {
            AccountingOffset = shape == 0 ? 1 : 0,
            BufferedCharacters = shape == 1 ? 1 : 0,
            WarningCount = shape == 2 ? 1 : 0,
            Succeeded = shape != 3,
            TelemetryCopies = shape == 4 ? 2 : 1
        };
        var result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(fixture), Token);
        Assert.False(result.Succeeded);
        var assertion = shape switch { 0 => "stream.accounting", 1 => "stream.buffer", 2 => "stream.warnings", 3 => "stream.completed", _ => "stream.dispatches" };
        QualificationScenarioV3Tests.Check(result, "chat.streaming-integrity@2", assertion, AssertionStatus.Failed);
    }

    [Fact]
    public async Task AbsentTelemetryIsInconclusiveAndCannotProveDispatches()
    {
        var result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(
            new(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop")) { EmitTelemetry = false }), Token);
        Assert.Equal(ContractDisposition.Inconclusive, result.Contracts[1].Disposition);
        QualificationScenarioV3Tests.Check(result, "chat.streaming-integrity@2", "stream.dispatches", AssertionStatus.NotObserved);
    }

    [Theory]
    [InlineData("wrong_tool", "{\"value\":\"baize-live\"}", "tool.name")]
    [InlineData("echo_value", "{", "tool.json")]
    [InlineData("echo_value", "{}", "tool.schema")]
    [InlineData("echo_value", "{\"value\":3}", "tool.schema")]
    [InlineData("echo_value", "{\"value\":\"wrong\"}", "tool.value")]
    [InlineData("echo_value", "{\"value\":\"baize-live\",\"extra\":true}", "tool.schema")]
    [InlineData("echo_value", "[]", "tool.schema")]
    public async Task OriginalCanonicalSchemaAndNameChecksStillRejectInvalidOutput(string name, string arguments, string check)
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(ToolCallDelta: new(0, "call_1", name, arguments)), new(FinishReason: "tool_calls"));
        var result = await QualificationScenariosV3.RunSingleToolAsync(QualificationScenarioV3Tests.Context(fixture, true), Token);
        Assert.False(result.Succeeded);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", check, AssertionStatus.Failed);
    }

    [Fact]
    public async Task InvalidSyntaxCannotInventDuplicateMemberOrValueObservations()
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(ToolCallDelta: new(0, "call_1", "echo_value", "{\"value\":\"baize-live\"")), new(FinishReason: "tool_calls"));
        var result = await QualificationScenariosV3.RunSingleToolAsync(QualificationScenarioV3Tests.Context(fixture, true), Token);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.json", AssertionStatus.Failed);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.json-unique", AssertionStatus.NotObserved);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.schema", AssertionStatus.NotObserved);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.value", AssertionStatus.NotObserved);
    }

    [Fact]
    public async Task BaselineAlsoRequiresBypassingRepairPath()
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop")
        { ContentRepairDiagnostics = new(LlmRepairShapeStatus.NotEvaluated, []) });
        var result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(fixture), Token);
        QualificationScenarioV3Tests.Check(result, "chat.baseline@3", "response.unrepaired", AssertionStatus.Failed);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("Unsupported")]
    [InlineData("Insufficient")]
    public async Task InapplicableBudgetsStillPreventInvocation(string budget)
    {
        var fixture = new ScenarioFixture();
        var result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(fixture) with
        { OutputBudget = Enum.Parse<OutputBudgetSupport>(budget) }, Token);
        Assert.Equal(0, fixture.Calls);
        Assert.All(result.Contracts, c => Assert.Equal(ContractDisposition.Blocked, c.Disposition));
    }

    [Fact]
    public async Task ToolsMustBeConfiguredAndPreflightDoesNotFabricateProvenance()
    {
        var fixture = new ScenarioFixture();
        var result = await QualificationScenariosV3.RunSingleToolAsync(QualificationScenarioV3Tests.Context(fixture), Token);
        Assert.Equal(0, fixture.Calls);
        Assert.Equal("native-tools-not-configured", result.Diagnostics.FailureCode);
        Assert.Equal(AssertionStatus.NotObserved, result.Identity.CanonicalCorrelation);
        Assert.Equal(AssertionStatus.NotObserved, result.Identity.ProviderIdentityStatus);
    }

    [Fact]
    public async Task PartialStreamPreservesConclusiveViolationsAndLateErrorsCannotPass()
    {
        var partial = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK"),
            new(ToolCallDelta: new(0, "call_1", "echo_value", "{}")))
        { Error = new LlmClientException("private", LlmClientFailureKind.Availability) };
        var result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(partial), Token);
        Assert.Equal(ContractDisposition.Blocked, result.Contracts[0].Disposition);
        QualificationScenarioV3Tests.Check(result, "chat.baseline@3", "response.shape", AssertionStatus.Failed);
        QualificationScenarioV3Tests.Check(result, "chat.baseline@3", "response.expected-value", AssertionStatus.NotObserved);
        var late = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK", FinishReason: "stop"))
        { Error = new LlmClientException("private", LlmClientFailureKind.Protocol) };
        result = await QualificationScenariosV3.RunBaselineAsync(QualificationScenarioV3Tests.Context(late), Token);
        Assert.False(result.Succeeded);
        Assert.True(late.Disposed);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result));
    }
}
