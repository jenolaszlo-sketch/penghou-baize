using Penghou.Baize.Qualification.Execution;

namespace Penghou.Baize.Qualification.Tests;

// Cancellation, incomplete observation and evidence-storage failure.
public sealed class Stage2CancellationAndEvidenceTests
{
    // 3. Cancellation preserves an incomplete observation and never reports
    // the run as a successful qualification.
    [Fact]
    public async Task Cancellation_PreservesIncompleteObservation()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-slow", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var faults = new ExecutionFaults { AfterIntentAppend = _ => cancellation.Cancel() };
#pragma warning disable xUnit1051 // The runner's own token is the operator signal under test.
        var run = await workspace.RunWithCancellationAsync("fake-slow", "chat.baseline@3", cancellation.Token, faults);
#pragma warning restore xUnit1051
        Assert.Equal(130, run.ExitCode);
        Assert.Equal("run-summary", run.Summary.GetProperty("kind").GetString());
        Assert.Equal(130, run.Summary.GetProperty("exitCode").GetInt32());

        var entries = Stage2Evidence.ReadAll(workspace);
        // The dispatch intent is durable; no result may fabricate a pass.
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(0, Stage2Evidence.Count(entries, EvidenceLedger.TrialFinished));
        Assert.Equal(0, Stage2Evidence.Count(entries, EvidenceLedger.RunFinished));
        Assert.True(Stage2Evidence.Count(entries, EvidenceLedger.AttemptFinished) <= 1);

        // The report remains incomplete and explicitly not green.
        var report = await workspace.ReportAsync();
        report.AssertCompleted("report-summary", 4);
        Assert.False(report.Summary.GetProperty("complete").GetBoolean());
        var reportCase = Stage2Evidence.Report(workspace).GetProperty("report").GetProperty("cases")[0];
        Assert.Equal("NotRun", reportCase.GetProperty("outcome").GetString());
        Assert.Equal("Cancelled", reportCase.GetProperty("workflowStatus").GetString());

        // A resume reconciles the interrupted attempt as conservative unknown
        // spend and never re-invokes the cancelled case.
        var resume = await workspace.ResumeAsync();
        resume.AssertCompleted("run-summary", 4);
        var afterResume = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(afterResume, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(afterResume, EvidenceLedger.AttemptInterrupted));
        Stage2Evidence.AssertLedgerVerified(workspace);
    }

    // 8. Evidence storage failure cannot produce a successful qualification
    // report: the run exits with an internal failure and no report exists.
    [Fact]
    public async Task EvidenceStorageFailure_ProducesNoSuccessfulReport()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var faults = new ExecutionFaults
        {
            FailEvidenceAppend = eventType => eventType == EvidenceLedger.AttemptFinished
        };
        var run = await workspace.RunAsync("fake-alpha", "chat.baseline@3", faults: faults);
        run.AssertFailed("evidence-verification-or-storage-failure", 5);
        Assert.False(File.Exists(Path.Combine(workspace.RunDir, "reports", "summary.json")),
            "no qualification report may exist after evidence storage failure");
        Assert.False(File.Exists(Path.Combine(workspace.RunDir, "reports", "report.md")));

        // The intent and the negative observation survive; recovery can then
        // proceed through an explicit resume.
        var entries = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(0, Stage2Evidence.Count(entries, EvidenceLedger.AttemptFinished));
    }

    // Timeout keeps the attempt bounded and blocked, never silently green.
    [Fact]
    public async Task AttemptTimeout_RecordsBoundedBlockedObservation()
    {
        using var workspace = new Stage2Workspace();
        var shortPolicy = Stage2Fixtures.Policy
            .Replace("\"attemptTimeout\": \"00:02:00\"", "\"attemptTimeout\": \"00:00:05\"", StringComparison.Ordinal)
            .Replace("\"streamIdleTimeout\": \"00:00:30\"", "\"streamIdleTimeout\": \"00:00:05\"", StringComparison.Ordinal);
        (await workspace.PlanAsync("fake-slow", "chat.baseline@3", policy: shortPolicy))
            .AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-slow", "chat.baseline@3");
        run.AssertCompleted("run-summary", 4);
        var finished = Stage2Evidence.Event(Stage2Evidence.ReadAll(workspace), EvidenceLedger.AttemptFinished);
        Assert.Equal("Blocked", finished.GetProperty("outcome").GetString());
        Assert.Equal("timeout", finished.GetProperty("failureCategory").GetString());
    }
}
