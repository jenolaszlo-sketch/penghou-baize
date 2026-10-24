using System.Text.Json;
using Penghou.Baize.Qualification;

namespace Penghou.Baize.Qualification.Tests;

public sealed class QualificationCompletionV3Tests
{
    public static TheoryData<bool, bool, string, bool?[], bool?[], bool> CompletionCases
    {
        get
        {
            var cases = new TheoryData<bool, bool, string, bool?[], bool?[], bool>();
            foreach (var tools in new[] { false, true })
                foreach (var routed in new[] { false, true })
                {
                    cases.Add(tools, routed, "unknown", [], [null, null], true);
                    cases.Add(tools, routed, "incomplete-control", [], [false], false);
                    cases.Add(tools, routed, "incomplete-then-metadata", [], [false, null], false);
                    cases.Add(tools, routed, "intermediate-then-complete", [false], [true], true);
                    cases.Add(tools, routed, "intermediate-metadata-then-complete", [false, null], [true, null], true);
                    cases.Add(tools, routed, "complete-then-metadata", [], [true, null], true);
                    cases.Add(tools, routed, "later-explicit-incomplete", [], [true, false, null], false);
                    cases.Add(tools, routed, "later-explicit-complete", [], [false, true, false, true, null], true);
                    cases.Add(tools, routed, "no-provider-diagnostics", [], [], true);
                }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(CompletionCases))]
    public async Task R1_LastExplicitCompletionSurvivesMetadata(bool tools, bool routed, string sequence,
        bool?[] beforePayload, bool?[] afterFinish, bool expected)
    {
        // Promotes the independent public-stream probes, including their final
        // one-success router snapshot. The real-router composition is also
        // exercised by QualificationProviderV3Tests.
        var events = beforePayload.Select(Done).ToList();
        events.Add(tools
            ? new(ToolCallDelta: new(0, "call_1", "echo_value", "{\"value\":\"baize-live\"}"))
            : new(Delta: "BAIZE_OK"));
        events.Add(new(FinishReason: tools ? "tool_calls" : "stop"));
        events.AddRange(afterFinish.Select(Done));
        if (routed)
            events.Add(new(RouterDiagnostics: new([
                new("fixture", "fixture", "fixture", LlmRouterAttemptOutcome.Succeeded, TimeSpan.Zero)
            ])));

        var fixture = new ScenarioFixture(events.ToArray());
        var context = QualificationScenarioV3Tests.Context(fixture, tools, router: routed);
        var result = tools
            ? await QualificationScenariosV3.RunSingleToolAsync(context, TestContext.Current.CancellationToken)
            : await QualificationScenariosV3.RunBaselineAsync(context, TestContext.Current.CancellationToken);

        Assert.True(expected == result.Succeeded, sequence + ": " + JsonSerializer.Serialize(result));
        var status = expected ? AssertionStatus.Passed : AssertionStatus.Failed;
        QualificationScenarioV3Tests.Check(result, tools ? "tools.single@3" : "chat.baseline@3", "execution.completed", status);
        QualificationScenarioV3Tests.Check(result, "chat.streaming-integrity@2", "stream.terminal", status);
        // Reject on the completion witness, not a broken reconstruction/router.
        QualificationScenarioV3Tests.Check(result, "chat.streaming-integrity@2", "stream.reconstruction", AssertionStatus.Passed);
        QualificationScenarioV3Tests.Check(result, tools ? "tools.single@3" : "chat.baseline@3", "diagnostics.router", AssertionStatus.Passed);
        Assert.Equal(beforePayload.Length + afterFinish.Length > 0, result.Diagnostics.ProviderDiagnosticsObserved);
        Assert.All(result.Contracts, c => Assert.Equal(expected ? ContractDisposition.Passed : ContractDisposition.Failed, c.Disposition));
        if (expected)
            Assert.All(result.Contracts.SelectMany(c => c.Assertions), a => Assert.Equal(AssertionStatus.Passed, a.Status));
        if (routed)
        {
            Assert.Single(result.Router.Snapshots);
            Assert.Equal(1, result.Router.SuccessfulAttempts);
            Assert.Equal(0, result.Router.FailedAttempts);
            Assert.True(result.Router.TerminalOrderValid);
        }
        Assert.Equal(1, fixture.Calls);
        Assert.True(fixture.Disposed);
    }

    private static LlmStreamEvent Done(bool? value) => new(Diagnostics: new("fixture", Done: value));
}
