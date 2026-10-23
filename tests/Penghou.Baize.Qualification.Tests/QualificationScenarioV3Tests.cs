using System.Diagnostics;
using System.Text.Json;
using Penghou.Baize.Qualification;

namespace Penghou.Baize.Qualification.Tests;

public sealed class QualificationScenarioV3Tests
{
    private const string Arguments = "{\"value\":\"baize-live\"}";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static LlmStreamEvent Call(string? id = "call_1", string arguments = Arguments) => new(ToolCallDelta: new(0, id, "echo_value", arguments));
    private static LlmStreamEvent Stop() => new(FinishReason: "stop");
    private static LlmStreamEvent Router(params LlmRouterAttemptOutcome[] outcomes) => new(RouterDiagnostics: new(
        outcomes.Select(outcome => new LlmRouterAttempt("private-endpoint", "private-model", "fixture", outcome, TimeSpan.Zero)).ToArray()));
    internal static QualificationContextV3 Context(ScenarioFixture fixture, bool tools = false,
        ToolIdentityProtocol protocol = ToolIdentityProtocol.OpenAiChatCompletions, bool router = false) =>
        new(fixture.Context().Stream, tools, router, IdentityProtocol: protocol);

    internal static void Check(QualificationResultV3 result, string contract, string id, AssertionStatus status, string? code = null)
    {
        var check = Assert.Single(Assert.Single(result.Contracts, c => c.ContractId == contract).Assertions, a => a.Id == id);
        Assert.Equal(status, check.Status);
        if (code is not null) Assert.Equal(code, check.Code);
    }

    [Fact]
    public async Task NewVersionsKeepFixturesAndOldBehaviorExplicitlySeparate()
    {
        var badBaseline = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK"), Call(), Stop());
        var old = await QualificationScenarios.RunBaselineAsync(badBaseline.Context(), Token);
        var current = await QualificationScenariosV3.RunBaselineAsync(Context(badBaseline), Token);
        Assert.True(old.Succeeded);
        Assert.Equal("chat.baseline@2", old.Contracts[0].ContractId);
        Assert.Equal("chat.streaming-integrity@1", old.Contracts[1].ContractId);
        Assert.Equal("chat.baseline@3", current.Contracts[0].ContractId);
        Assert.Equal("chat.streaming-integrity@2", current.Contracts[1].ContractId);
        Check(current, "chat.baseline@3", "response.shape", AssertionStatus.Failed, "undeclared-tool-call");

        var badTool = new ScenarioFixture(new LlmStreamEvent(Delta: "I refuse to call the tool."), Call(), Stop());
        Assert.True((await QualificationScenarios.RunSingleToolAsync(badTool.Context(true), Token)).Succeeded);
        var tool = await QualificationScenariosV3.RunSingleToolAsync(Context(badTool, true), Token);
        Check(tool, "tools.single@3", "response.shape", AssertionStatus.Failed, "unexpected-answer-content");
        Assert.False(tool.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public async Task ToolOnlyAllowsFormattingWhitespaceAndSeparateReasoning(string text)
    {
        var fixture = new ScenarioFixture(new LlmStreamEvent(Delta: text, ReasoningContent: "thinking"), Call(), Stop());
        var result = await QualificationScenariosV3.RunSingleToolAsync(Context(fixture, true), Token);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(1, fixture.Calls);
        Assert.Equal(512, fixture.Request!.MaxTokens);
        Assert.True(fixture.Disposed);
        Assert.Equal(AssertionStatus.NotObserved, result.Identity.ProviderIdentityStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task F3_ContradictoryRouterEvidenceCannotPass(int shape)
    {
        LlmStreamEvent[] events = shape switch
        {
            0 => [new(Delta: "BAIZE_OK"), Stop(), Router()],
            1 => [new(Delta: "BAIZE_OK"), Stop(), Router(LlmRouterAttemptOutcome.Failed)],
            2 => [new(Delta: "BAIZE_OK"), Stop(), Router(LlmRouterAttemptOutcome.Failed), Router(LlmRouterAttemptOutcome.Succeeded)],
            3 => [new(Delta: "BAIZE_OK"), Stop(), Router(LlmRouterAttemptOutcome.Succeeded), Router(LlmRouterAttemptOutcome.Succeeded)],
            4 => [Router(LlmRouterAttemptOutcome.Succeeded), new(Delta: "BAIZE_OK"), Stop()],
            _ => [new(Delta: "BAIZE_OK"), Stop(), Router((LlmRouterAttemptOutcome)999)]
        };
        var result = await QualificationScenariosV3.RunBaselineAsync(Context(new(events), router: true), Token);
        Assert.False(result.Succeeded);
        Check(result, "chat.baseline@3", "diagnostics.router", AssertionStatus.Failed);
        Check(result, "chat.baseline@3", "execution.completed", AssertionStatus.Failed);
        Check(result, "chat.streaming-integrity@2", "stream.dispatches", AssertionStatus.Failed);
        if (shape == 2)
        {
            Assert.Equal(1, result.Router.Snapshots[0].FailedAttempts);
            Assert.Equal(1, result.Router.Snapshots[1].SuccessfulAttempts);
        }
        Assert.DoesNotContain("private-endpoint", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task F3_OneSuccessfulFinalAttemptPassesAndMultipleAttemptsFail()
    {
        var valid = await QualificationScenariosV3.RunBaselineAsync(Context(new(new LlmStreamEvent(Delta: "BAIZE_OK"), Stop(),
            Router(LlmRouterAttemptOutcome.Succeeded)), router: true), Token);
        Assert.True(valid.Succeeded, JsonSerializer.Serialize(valid));
        Assert.Equal(1, valid.Router.SuccessfulAttempts);
        Assert.Equal(0, valid.Router.FailedAttempts);
        Assert.True(valid.Router.TerminalOrderValid);
        var multiple = await QualificationScenariosV3.RunBaselineAsync(Context(new(new LlmStreamEvent(Delta: "BAIZE_OK"), Stop(),
            Router(LlmRouterAttemptOutcome.Failed, LlmRouterAttemptOutcome.Succeeded)), router: true), Token);
        Check(multiple, "chat.streaming-integrity@2", "stream.dispatches", AssertionStatus.Failed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task F3_SuppliedFailedRouterCannotHideBehindOptionalDiagnostics(bool tools, bool required)
    {
        var fixture = new ScenarioFixture(tools ? Call() : new LlmStreamEvent(Delta: "BAIZE_OK"), Stop(), Router(LlmRouterAttemptOutcome.Failed));
        var context = Context(fixture, tools, router: required);
        var result = tools ? await QualificationScenariosV3.RunSingleToolAsync(context, Token) : await QualificationScenariosV3.RunBaselineAsync(context, Token);
        Assert.False(result.Succeeded);
        Check(result, tools ? "tools.single@3" : "chat.baseline@3", "diagnostics.router", AssertionStatus.Failed);
    }

    [Fact]
    public async Task PayloadAndFinishInOnePublicEventRemainsValid()
    {
        var result = await QualificationScenariosV3.RunSingleToolAsync(Context(new(Call() with { FinishReason = "tool_calls" }), true), Token);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F4_ForeignAndBareCancellationRemainInconclusive(bool foreign)
    {
        var fixture = new ScenarioFixture() { Error = foreign ? new OperationCanceledException(new CancellationToken(true)) : new OperationCanceledException() };
        var result = await QualificationScenariosV3.RunBaselineAsync(Context(fixture), Token);
        Assert.All(result.Contracts, c => Assert.Equal(ContractDisposition.Inconclusive, c.Disposition));
        Assert.Equal("ambiguous-cancellation", result.Diagnostics.FailureCode);
        Check(result, "chat.baseline@3", "response.expected-value", AssertionStatus.NotObserved);
        Check(result, "chat.streaming-integrity@2", "stream.dispatches", AssertionStatus.NotObserved);
        Assert.True(fixture.Disposed);
    }

    [Fact]
    public async Task F4_ExplicitDeadlineAndCallerCancellationAreDistinctAndDispose()
    {
        using var deadline = new CancellationTokenSource();
        var timed = new ScenarioFixture(new LlmStreamEvent(Delta: "BAIZE_OK")) { BeforeCompletion = deadline.Cancel };
        var timeout = await QualificationScenariosV3.RunBaselineAsync(Context(timed) with { Deadline = deadline.Token }, Token);
        Assert.Equal("timeout", timeout.Diagnostics.FailureCode);
        Assert.All(timeout.Contracts, c => Assert.Equal(ContractDisposition.Blocked, c.Disposition));
        Assert.True(timed.Disposed);
        using var caller = new CancellationTokenSource();
        var cancelled = new ScenarioFixture() { BeforeCompletion = caller.Cancel };
        var result = await QualificationScenariosV3.RunBaselineAsync(Context(cancelled), caller.Token);
        Assert.Equal("caller-cancelled", result.Diagnostics.FailureCode);
        Assert.All(result.Contracts, c => Assert.Equal(ContractDisposition.Cancelled, c.Disposition));
        Assert.True(cancelled.Disposed);
        var typed = await QualificationScenariosV3.RunBaselineAsync(Context(new() { Error = new TimeoutException("private") }), Token);
        Assert.Equal("timeout", typed.Diagnostics.FailureCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeDispatchIsExplicit(bool callerCancelled)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var fixture = new ScenarioFixture();
        var result = await QualificationScenariosV3.RunBaselineAsync(Context(fixture) with { Deadline = cancelled.Token },
            callerCancelled ? cancelled.Token : Token);
        Assert.Equal(0, fixture.Calls);
        Assert.Equal(callerCancelled ? "caller-cancelled" : "timeout", result.Diagnostics.FailureCode);
        Assert.All(result.Contracts.SelectMany(c => c.Assertions), a => Assert.Equal(AssertionStatus.NotObserved, a.Status));
    }

    [Theory]
    [InlineData("OpenAiChatCompletions", false)]
    [InlineData("AnthropicMessages", false)]
    [InlineData("OllamaChat", true)]
    [InlineData("GeminiGenerateContent", true)]
    public async Task D1_MissingStreamIdentityIsProtocolScoped(string protocolValue, bool canCorrelate)
    {
        var protocol = Enum.Parse<ToolIdentityProtocol>(protocolValue);
        var result = await QualificationScenariosV3.RunSingleToolAsync(Context(new(Call(null), Stop()), true, protocol), Token);
        Assert.Equal(canCorrelate, result.Succeeded);
        Assert.Equal(AssertionStatus.NotObserved, result.Identity.ProviderIdentityStatus);
        if (canCorrelate) Assert.Equal(CanonicalIdentityOrigin.CollectorAssigned, result.Identity.CanonicalOrigin);
        else Check(result, "tools.single@3", "tool.identity", AssertionStatus.Failed);
        Assert.Equal(protocol == ToolIdentityProtocol.OllamaChat ? IdentityApplicability.NotApplicable : IdentityApplicability.Applicable,
            result.Identity.ProviderIdentityApplicability);
    }

    [Fact]
    public async Task D1_UnspecifiedProtocolBlocksWithoutDispatch()
    {
        var fixture = new ScenarioFixture();
        var result = await QualificationScenariosV3.RunSingleToolAsync(Context(fixture, true, ToolIdentityProtocol.Unspecified), Token);
        Assert.Equal(0, fixture.Calls);
        Assert.Equal("identity-protocol-unspecified", result.Diagnostics.FailureCode);
        Assert.All(result.Contracts, c => Assert.Equal(ContractDisposition.Blocked, c.Disposition));
    }

    [Theory]
    [InlineData("{\"value\":\"WRONG\",\"value\":\"baize-live\"}")]
    [InlineData("{\"value\":\"baize-live\",\"value\":\"WRONG\"}")]
    [InlineData("{\"value\":\"baize-live\",\"value\":\"baize-live\"}")]
    [InlineData("{\"value\":\"WRONG\",\"\\u0076alue\":\"baize-live\"}")]
    [InlineData("{\"value\":\"baize-live\",\"extra\":{\"x\":1,\"x\":2}}")]
    [InlineData("{\"value\":\"baize-live\",\"extra\":[{\"x\":1,\"x\":2}]}")]
    public async Task DuplicatePropertiesNeverUseLastMemberToManufactureSuccess(string json)
    {
        var result = await QualificationScenariosV3.RunSingleToolAsync(Context(new(Call(arguments: json), Stop()), true), Token);
        Check(result, "tools.single@3", "tool.json-unique", AssertionStatus.Failed, "duplicate-json-property");
        Check(result, "tools.single@3", "tool.value", AssertionStatus.NotObserved);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task RepairPathEvidenceCannotBecomeNativeSuccess(int shape)
    {
        var evt = shape switch
        {
            0 => Call() with { ContentWasRepaired = true },
            1 => Call() with { ContentRepairAttempts = [new("repair", LlmRepairStatus.Succeeded, Arguments)] },
            2 => Call() with { ContentRepairDiagnostics = new(LlmRepairShapeStatus.Matched, [], "repair") { IsRepairAccepted = true } },
            3 => Call() with { ContentRepairDiagnostics = new(LlmRepairShapeStatus.NotEvaluated, [], TolerantRecovery: new(true, "recovered", 1, 0, ["private"])) },
            _ => Call() with { ContentRepairAttempts = [new("not-run", LlmRepairStatus.Skipped)] }
        };
        var result = await QualificationScenariosV3.RunSingleToolAsync(Context(new(evt, Stop()), true), Token);
        Check(result, "tools.single@3", "tool.unrepaired", AssertionStatus.Failed, "repair-path-observed");
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("ordinary", true)]
    [InlineData("with space", true)]
    [InlineData("line\nbreak", true)]
    [InlineData(" ", true)]
    [InlineData("", false)]
    public async Task OpaqueIdsAreComparedExactlyWithoutInventedFormatRules(string id, bool expected)
    {
        var result = await QualificationScenariosV3.RunSingleToolAsync(Context(new(Call(id), Stop()), true), Token);
        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task LongOpaqueIdIsAcceptedButNotRetainedInEvidence()
    {
        var id = new string('x', 1024);
        var result = await QualificationScenariosV3.RunSingleToolAsync(Context(new(Call(id), Stop()), true), Token);
        Assert.True(result.Succeeded);
        Assert.DoesNotContain(id, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task KnownToolIdentityConflictsAndMovingPartsRemainFailures()
    {
        var conflict = await QualificationScenariosV3.RunSingleToolAsync(Context(new(Call(),
            new(ToolCallDelta: new(0, "changed")), Stop()), true), Token);
        Check(conflict, "tools.single@3", "tool.identity", AssertionStatus.Failed);
        var moved = await QualificationScenariosV3.RunSingleToolAsync(Context(new(
            new LlmStreamEvent(ToolCallDelta: new(0, "call_1", "echo_value", "{\"value\":")) { PartIndex = 0 },
            new(ToolCallDelta: new(0, ArgumentsJsonFragment: "\"baize-live\"}")) { PartIndex = 1 }, Stop()), true), Token);
        Check(moved, "chat.streaming-integrity@2", "stream.sequence", AssertionStatus.Failed);
        Assert.False(moved.Succeeded);
    }

    [Theory]
    [InlineData(400, "Blocked")]
    [InlineData(404, "Blocked")]
    [InlineData(401, "Blocked")]
    [InlineData(503, "Blocked")]
    [InlineData(0, "Inconclusive")]
    public async Task NoOutputExecutionFailuresDoNotManufactureModelAssertions(int status, string dispositionValue)
    {
        var disposition = Enum.Parse<ContractDisposition>(dispositionValue);
        var fixture = new ScenarioFixture() { Error = status == 0 ? new InvalidOperationException("private") : new LlmClientException("private", status) };
        var result = await QualificationScenariosV3.RunBaselineAsync(Context(fixture, router: true), Token);
        Assert.All(result.Contracts, c => Assert.Equal(disposition, c.Disposition));
        Check(result, "chat.baseline@3", "response.expected-value", AssertionStatus.NotObserved);
        Check(result, "chat.streaming-integrity@2", "stream.reconstruction", AssertionStatus.NotObserved);
        Check(result, "chat.streaming-integrity@2", "stream.dispatches", AssertionStatus.NotObserved);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task DescriptorsMatchActualChecksAndParallelExecutionsRemainIsolated()
    {
        using var parent = new Activity("caller").Start();
        var baseline = new ScenarioFixture(new LlmStreamEvent(Delta: "\r\nBAIZE_OK\t"), Stop());
        var tool = new ScenarioFixture(Call(), Stop());
        var results = await Task.WhenAll(QualificationScenariosV3.RunBaselineAsync(Context(baseline), Token),
            QualificationScenariosV3.RunSingleToolAsync(Context(tool, true), Token));
        Assert.All(results, r => Assert.True(r.Succeeded, JsonSerializer.Serialize(r)));
        foreach (var descriptor in new[] { QualificationContractsV3.Baseline, QualificationContractsV3.SingleTool, QualificationContractsV3.Streaming })
        {
            var result = results.SelectMany(r => r.Contracts).First(c => c.ContractId == descriptor.Id);
            Assert.Equal(descriptor.AssertionIds, result.Assertions.Select(a => a.Id));
            Assert.Equal(512, descriptor.MaximumOutputTokens);
            Assert.Equal(1, descriptor.MaximumDispatches);
        }
        Assert.All(results, r => Assert.Single(r.Diagnostics.Integrity));
        Assert.Same(parent, Activity.Current);
    }
}
