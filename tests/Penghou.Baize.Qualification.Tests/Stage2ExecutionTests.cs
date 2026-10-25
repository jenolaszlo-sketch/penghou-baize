using System.Text.Json;
using Penghou.Baize.Qualification.Execution;

namespace Penghou.Baize.Qualification.Tests;

// Deterministic Stage 2 acceptance: frozen plan -> Zhinu workflow -> Baize
// synthetic invocation -> BQ-1a assertions -> Siming evidence -> report.
public sealed class Stage2ExecutionTests
{
    // 1. Successful workflow and evidence recording.
    [Fact]
    public async Task BaselineSyntheticCase_CompletesWithVerifiedEvidenceAndReport()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-alpha", "chat.baseline@3");
        run.AssertCompleted("run-summary", 0);
        Assert.Equal("all-selected-cases-passed", run.Summary.GetProperty("outcome").GetString());
        Assert.True(run.Summary.GetProperty("ledgerVerified").GetBoolean());
        Assert.True(run.Summary.GetProperty("checkpointCaptured").GetBoolean());

        var entries = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.RunStarted));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.RunFinished));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptReserved));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptFinished));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.UsageSettled));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.TrialFinished));
        Assert.Equal(0, Stage2Evidence.Count(entries, EvidenceLedger.AttemptInterrupted));
        Assert.Equal(0, Stage2Evidence.Count(entries, EvidenceLedger.AttemptReleased));
        Assert.Equal(0, Stage2Evidence.Count(entries, EvidenceLedger.BudgetDenied));

        var finished = Stage2Evidence.Event(entries, EvidenceLedger.AttemptFinished);
        Assert.Equal("Passed", finished.GetProperty("outcome").GetString());
        Assert.True(finished.GetProperty("modelInvoked").GetBoolean());
        Assert.Equal("Complete", finished.GetProperty("completeness").GetString());
        var diagnostics = finished.GetProperty("diagnostics");
        Assert.Equal(1, diagnostics.GetProperty("invocationCount").GetInt32());
        Assert.Equal(1, diagnostics.GetProperty("routerAttemptCount").GetInt32());
        var usage = finished.GetProperty("usage");
        Assert.Equal("Complete", usage.GetProperty("completeness").GetString());
        Assert.Equal(12, usage.GetProperty("promptTokens").GetInt64());
        Assert.Equal(3, usage.GetProperty("completionTokens").GetInt64());
        var assertions = finished.GetProperty("assertions").EnumerateArray().ToArray();
        Assert.All(assertions, assertion => Assert.Equal("Passed", assertion.GetProperty("status").GetString()));
        Assert.Equal(7, assertions.Length);

        var trial = Stage2Evidence.Event(entries, EvidenceLedger.TrialFinished);
        Assert.Equal("Passed", trial.GetProperty("outcome").GetString());
        Assert.True(trial.GetProperty("complete").GetBoolean());
        Stage2Evidence.AssertLedgerVerified(workspace);

        var report = Stage2Evidence.Report(workspace).GetProperty("report");
        Assert.Equal(1, report.GetProperty("passed").GetInt32());
        Assert.True(report.GetProperty("ledgerVerified").GetBoolean());
        Assert.True(report.GetProperty("complete").GetBoolean());
        var reportCase = report.GetProperty("cases")[0];
        Assert.Equal("Passed", reportCase.GetProperty("outcome").GetString());
        Assert.Equal("synthetic/model-alpha", reportCase.GetProperty("providerModel").GetString());
        Assert.Equal("fake-alpha-endpoint", reportCase.GetProperty("endpointId").GetString());
        Assert.Equal("SyntheticFake", reportCase.GetProperty("transportProfile").GetString());
        var markdown = Stage2Evidence.Markdown(workspace);
        Assert.Contains("Outcome: **Passed**", markdown);
        Assert.Contains("QualificationDispatchIntent", markdown);
    }

    // 1b. Tool contract through the same fake provider.
    [Fact]
    public async Task ToolSyntheticCase_PassesSingleToolContract()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "tools.single@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-alpha", "tools.single@3");
        run.AssertCompleted("run-summary", 0);
        var finished = Stage2Evidence.Event(Stage2Evidence.ReadAll(workspace), EvidenceLedger.AttemptFinished);
        Assert.Equal("Passed", finished.GetProperty("outcome").GetString());
        var assertions = finished.GetProperty("assertions").EnumerateArray().ToArray();
        Assert.Equal(12, assertions.Length);
        Assert.Contains(assertions, assertion => assertion.GetProperty("id").GetString() == "tool.value"
            && assertion.GetProperty("status").GetString() == "Passed");
    }

    // 2. Contract failure with preserved negative evidence.
    [Fact]
    public async Task WrongValueBaseline_FailsWithPreservedNegativeAssertions()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-wrong", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-wrong", "chat.baseline@3");
        run.AssertCompleted("run-summary", 3);
        Assert.Equal("contract-failure", run.Summary.GetProperty("outcome").GetString());

        var entries = Stage2Evidence.ReadAll(workspace);
        var finished = Stage2Evidence.Event(entries, EvidenceLedger.AttemptFinished);
        Assert.Equal("Failed", finished.GetProperty("outcome").GetString());
        Assert.Equal("contract-assertion-failure", finished.GetProperty("attribution").GetString());
        var failed = finished.GetProperty("assertions").EnumerateArray()
            .Where(assertion => assertion.GetProperty("status").GetString() == "Failed")
            .Select(assertion => assertion.GetProperty("id").GetString()!).ToArray();
        Assert.Contains("response.expected-value", failed);
        var trial = Stage2Evidence.Event(entries, EvidenceLedger.TrialFinished);
        Assert.Equal("Failed", trial.GetProperty("outcome").GetString());
        Assert.Contains("response.expected-value", trial.GetProperty("failedAssertionIds").EnumerateArray()
            .Select(id => id.GetString()!).ToArray());
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.RunFinished));
        Stage2Evidence.AssertLedgerVerified(workspace);

        var reportCase = Stage2Evidence.Report(workspace).GetProperty("report").GetProperty("cases")[0];
        Assert.Equal("Failed", reportCase.GetProperty("outcome").GetString());
        Assert.Contains("response.expected-value", reportCase.GetProperty("failedAssertionIds").EnumerateArray()
            .Select(id => id.GetString()!).ToArray());
    }

    // Provider failure must not become a model capability failure.
    [Fact]
    public async Task UnavailableProvider_IsBlockedNotFailed()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-unavailable", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-unavailable", "chat.baseline@3");
        run.AssertCompleted("run-summary", 4);
        var finished = Stage2Evidence.Event(Stage2Evidence.ReadAll(workspace), EvidenceLedger.AttemptFinished);
        Assert.Equal("Blocked", finished.GetProperty("outcome").GetString());
        Assert.Equal("provider-unavailable", finished.GetProperty("failureCategory").GetString());
        Assert.Equal("not-attributed-infrastructure-or-incomplete", finished.GetProperty("attribution").GetString());
        var reportCase = Stage2Evidence.Report(workspace).GetProperty("report").GetProperty("cases")[0];
        Assert.Equal("Blocked", reportCase.GetProperty("outcome").GetString());
    }

    // 9. Budget exhaustion and unknown usage.
    [Fact]
    public async Task BudgetExhaustion_PreventsFurtherExecutionAndSurvivesResume()
    {
        using var workspace = new Stage2Workspace();
        var tightPolicy = Stage2Fixtures.Policy.Replace("\"maximumModelCalls\": 12", "\"maximumModelCalls\": 1",
            StringComparison.Ordinal).Replace("\"maximumHttpRequests\": 12", "\"maximumHttpRequests\": 1",
            StringComparison.Ordinal);
        (await workspace.PlanAsync("fake-alpha,fake-wrong", "chat.baseline@3", policy: tightPolicy))
            .AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-alpha,fake-wrong", "chat.baseline@3");
        run.AssertCompleted("run-summary", 4);

        var entries = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptReserved));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.BudgetDenied));
        var denied = Stage2Evidence.Event(entries, EvidenceLedger.BudgetDenied);
        Assert.Equal("budget-model-calls-exhausted", denied.GetProperty("reasonCode").GetString());
        var deniedTrial = Stage2Evidence.Events(entries, EvidenceLedger.TrialFinished)
            .Single(trial => trial.GetProperty("modelId").GetString() == "fake-wrong");
        Assert.Equal("Blocked", deniedTrial.GetProperty("outcome").GetString());

        // Restarting the runner does not replenish the allowance.
        var resume = await workspace.ResumeAsync();
        resume.AssertCompleted("run-summary", 4);
        var afterResume = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(afterResume, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(afterResume, EvidenceLedger.BudgetDenied));
    }

    // 9b. Unknown usage retains conservative liability.
    [Fact]
    public async Task MissingUsage_IsSettledAsSpentUnknown()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-no-usage", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-no-usage", "chat.baseline@3");
        run.AssertCompleted("run-summary", 0);
        var entries = Stage2Evidence.ReadAll(workspace);
        var finished = Stage2Evidence.Event(entries, EvidenceLedger.AttemptFinished);
        Assert.Equal("Unknown", finished.GetProperty("usage").GetProperty("completeness").GetString());
        var settled = Stage2Evidence.Event(entries, EvidenceLedger.UsageSettled);
        Assert.Equal("spent-unknown", settled.GetProperty("settlement").GetString());
        Assert.Equal("Unknown", settled.GetProperty("usage").GetProperty("completeness").GetString());
        var reportCase = Stage2Evidence.Report(workspace).GetProperty("report").GetProperty("cases")[0];
        var attempt = reportCase.GetProperty("attempts")[0];
        Assert.Equal("Unknown", attempt.GetProperty("usageCompleteness").GetString());
        Assert.Equal("spent-unknown", attempt.GetProperty("settlement").GetString());
    }

    // 10. Multiple selected synthetic cases without identity or budget
    // contamination; a mixed outcome never masks the failing case.
    [Fact]
    public async Task MultipleCases_KeepSeparateIdentitiesAndMixedOutcomeIsVisible()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha,fake-wrong", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-alpha,fake-wrong", "chat.baseline@3");
        run.AssertCompleted("run-summary", 3);

        var entries = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(2, Stage2Evidence.Count(entries, EvidenceLedger.AttemptReserved));
        Assert.Equal(2, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(2, Stage2Evidence.Count(entries, EvidenceLedger.TrialFinished));
        var trials = Stage2Evidence.Events(entries, EvidenceLedger.TrialFinished);
        var alpha = trials.Single(trial => trial.GetProperty("modelId").GetString() == "fake-alpha");
        var wrong = trials.Single(trial => trial.GetProperty("modelId").GetString() == "fake-wrong");
        Assert.Equal("Passed", alpha.GetProperty("outcome").GetString());
        Assert.Equal("Failed", wrong.GetProperty("outcome").GetString());
        Assert.NotEqual(alpha.GetProperty("caseId").GetString(), wrong.GetProperty("caseId").GetString());
        Assert.NotEqual(alpha.GetProperty("trialId").GetString(), wrong.GetProperty("trialId").GetString());

        // Each case lives in its own trial stream and never shares an attempt.
        var attempts = Stage2Evidence.Events(entries, EvidenceLedger.AttemptFinished);
        Assert.Equal(2, attempts.Select(attempt => attempt.GetProperty("attemptId").GetString()).Distinct().Count());
        foreach (var attempt in attempts)
        {
            Assert.Equal(1, attempt.GetProperty("diagnostics").GetProperty("invocationCount").GetInt32());
            Assert.Equal(1, attempt.GetProperty("diagnostics").GetProperty("routerAttemptCount").GetInt32());
        }
        var runFinished = Stage2Evidence.Event(entries, EvidenceLedger.RunFinished);
        Assert.Equal(2L, runFinished.GetProperty("consumedModelCalls").GetInt64());
        Assert.Equal(2L, runFinished.GetProperty("consumedHttpRequests").GetInt64());

        var report = Stage2Evidence.Report(workspace).GetProperty("report");
        Assert.Equal(1, report.GetProperty("passed").GetInt32());
        Assert.Equal(1, report.GetProperty("failed").GetInt32());
        Assert.Equal(2, report.GetProperty("cases").GetArrayLength());
    }
}
