using System.Text.Json;
using Penghou.Baize.Qualification.Execution;

namespace Penghou.Baize.Qualification.Tests;

// Deterministic recovery and consistency: real Zhinu and Siming persistence,
// no mocks of either component. Faults are injected at the documented seams.
public sealed class Stage2RecoveryTests
{
    // 4. Process restart after a completed Zhinu step: the runner crashes
    // after all workflows complete; resume finishes the run without a second
    // model invocation.
    [Fact]
    public async Task CrashAfterWorkflowsCompleted_ResumeCompletesWithoutReinvocation()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var crash = new ExecutionFaults { AfterWorkflowsCompleted = () => throw new SimulatedProcessCrashException("after-workflows") };
        await Assert.ThrowsAsync<SimulatedProcessCrashException>(async () =>
            await workspace.RunAsync("fake-alpha", "chat.baseline@3", faults: crash));
        // The process is gone; its partial state is what a restart sees.
        var partial = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(partial, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(partial, EvidenceLedger.TrialFinished));

        var resume = await workspace.ResumeAsync();
        resume.AssertCompleted("run-summary", 0);
        var entries = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptFinished));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.TrialFinished));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.RunFinished));
        Stage2Evidence.AssertLedgerVerified(workspace);
    }

    // 6. Repeated Siming append with the same event identity: resuming twice
    // never duplicates any event.
    [Fact]
    public async Task RepeatedResume_NeverDuplicatesEvidence()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        (await workspace.ResumeAsync()).AssertCompleted("run-summary", 0);
        (await workspace.ResumeAsync()).AssertCompleted("run-summary", 0);
        var entries = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.RunStarted));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.RunFinished));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptReserved));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptFinished));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.UsageSettled));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.TrialFinished));
        Stage2Evidence.AssertLedgerVerified(workspace);
    }

    // 5. Failure between workflow completion and the Siming append: the
    // finalize append fails once; resume replays the committed steps and the
    // idempotent append, without another model invocation.
    [Fact]
    public async Task TrialFinishedAppendFailure_ResumeReplaysIdempotently()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var failedOnce = 0;
        var faults = new ExecutionFaults
        {
            FailEvidenceAppend = eventType =>
            {
                if (eventType == EvidenceLedger.TrialFinished && Interlocked.Increment(ref failedOnce) == 1)
                    return true;
                return false;
            }
        };
        var run = await workspace.RunAsync("fake-alpha", "chat.baseline@3", faults: faults);
        run.AssertFailed("evidence-verification-or-storage-failure", 5);
        var partial = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(partial, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(partial, EvidenceLedger.AttemptFinished));
        Assert.Equal(0, Stage2Evidence.Count(partial, EvidenceLedger.TrialFinished));

        var resume = await workspace.ResumeAsync();
        resume.AssertCompleted("run-summary", 0);
        var entries = Stage2Evidence.ReadAll(workspace);
        Assert.True(Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent) == 1,
            string.Join(" | ", entries.Select(e => $"{e.EventType}:{e.StreamId}")));
        Assert.True(Stage2Evidence.Count(entries, EvidenceLedger.TrialFinished) == 1,
            string.Join(" | ", entries.Select(e => $"{e.EventType}:{e.StreamId}")));
        // The interrupted run recorded its incomplete summary; the resume
        // appends the superseding complete one. History is never rewritten.
        var runFinished = Stage2Evidence.Events(entries, EvidenceLedger.RunFinished).Last();
        Assert.True(runFinished.GetProperty("complete").GetBoolean());
        Assert.Equal(1L, runFinished.GetProperty("consumedModelCalls").GetInt64());
        Stage2Evidence.AssertLedgerVerified(workspace);
    }

    // 7. A legitimate re-execution is a distinct actual attempt: interruption
    // after the durable dispatch intent; resume re-executes and both attempts
    // remain identifiable, the first interrupted with conservative liability.
    [Fact]
    public async Task InterruptionAfterIntent_ResumeCreatesDistinctAttempt()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        using var interrupt = new CancellationTokenSource();
        var faults = new ExecutionFaults
        {
            Interrupt = interrupt,
            AfterIntentAppend = _ => interrupt.Cancel()
        };
        await Assert.ThrowsAsync<SimulatedProcessCrashException>(async () =>
            await workspace.RunAsync("fake-alpha", "chat.baseline@3", faults: faults));

        var partial = Stage2Evidence.ReadAll(workspace);
        Assert.Equal(1, Stage2Evidence.Count(partial, EvidenceLedger.AttemptReserved));
        Assert.Equal(1, Stage2Evidence.Count(partial, EvidenceLedger.DispatchIntent));
        Assert.Equal(0, Stage2Evidence.Count(partial, EvidenceLedger.AttemptFinished));

        var resume = await workspace.ResumeAsync();
        resume.AssertCompleted("run-summary", 0);
        var entries = Stage2Evidence.ReadAll(workspace);
        // Two reservations, two intents: the interrupted one retains its
        // liability, the resumed one is a distinct actual attempt.
        Assert.Equal(2, Stage2Evidence.Count(entries, EvidenceLedger.AttemptReserved));
        Assert.Equal(2, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptFinished));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptInterrupted));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.TrialFinished));
        var attempts = Stage2Evidence.Events(entries, EvidenceLedger.AttemptFinished);
        Assert.Single(attempts);
        var interrupted = Stage2Evidence.Event(entries, EvidenceLedger.AttemptInterrupted);
        Assert.Equal("intent-without-result", interrupted.GetProperty("reasonCode").GetString());
        // The interrupted attempt settled as unknown spend.
        var settles = Stage2Evidence.Events(entries, EvidenceLedger.UsageSettled);
        Assert.Equal(2, settles.Length);
        Assert.Contains(settles, settle => settle.GetProperty("settlement").GetString() == "spent-unknown");
        Assert.Contains(settles, settle => settle.GetProperty("settlement").GetString() == "observed");
        // Budget retains both consumed slots: the first was never released.
        var runFinished = Stage2Evidence.Event(entries, EvidenceLedger.RunFinished);
        Assert.Equal(2L, runFinished.GetProperty("consumedModelCalls").GetInt64());
        Stage2Evidence.AssertLedgerVerified(workspace);

        // The report shows both attempts and does not fabricate a second trial.
        var reportCase = Stage2Evidence.Report(workspace).GetProperty("report").GetProperty("cases")[0];
        Assert.Equal(2, reportCase.GetProperty("attempts").GetArrayLength());
        var reportAttempts = reportCase.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Contains(reportAttempts, attempt => attempt.GetProperty("interrupted").GetBoolean());
        Assert.Contains(reportAttempts, attempt => attempt.GetProperty("outcome").GetString() == "Passed");
    }

    // 4b. Restart after a committed step with a different build is refused.
    [Fact]
    public async Task ModifiedPlanBytes_RefuseExecutionBeforeSecrets()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var planPath = Path.Combine(workspace.PlanDir, "plan.json");
        var bytes = File.ReadAllBytes(planPath);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(planPath, bytes);
        var run = await workspace.RunAsync("fake-alpha", "chat.baseline@3");
        run.AssertFailed("plan-identity-mismatch", 2);
        Assert.False(Directory.Exists(workspace.RunDir), "no run directory may exist after identity rejection");
    }

    // Resume refuses a manifest whose plan copy was tampered with.
    [Fact]
    public async Task TamperedRunPlan_RefusesResume()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        var storedPlan = Path.Combine(workspace.RunDir, "plan.json");
        var bytes = File.ReadAllBytes(storedPlan);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(storedPlan, bytes);
        var resume = await workspace.ResumeAsync();
        resume.AssertFailed("plan-identity-mismatch", 2);
    }

    // Deterministic privacy: no raw prompt text, sentinel secrets or request
    // payloads appear anywhere in the run directory.
    [Fact]
    public async Task SentinelAndPromptText_NeverPersist()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        (await workspace.ResumeAsync()).AssertCompleted("run-summary", 0);
        foreach (var file in Directory.EnumerateFiles(workspace.RunDir, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)))
        {
            if (file.EndsWith("workflow.db", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith("evidence.db", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = ReadShared(file);
                Assert.False(Contains(bytes, "BAIZE_OK"u8), $"prompt content persisted in {file}");
                Assert.False(Contains(bytes, "synthetic-not-a-credential"u8), $"sentinel persisted in {file}");
                Assert.False(Contains(bytes, "Reply with the exact token"u8), $"fixture prompt persisted in {file}");
            }
            else
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("BAIZE_OK", text);
                Assert.DoesNotContain("synthetic-not-a-credential", text);
                Assert.DoesNotContain("Reply with the exact token", text);
            }
        }
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return true;
        return false;
    }

    // Evidence tampering is detected by verification and blocks a green report.
    [Fact]
    public async Task TamperedEvidenceLedger_FailsVerificationAndReport()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        var database = Path.Combine(workspace.RunDir, "evidence.db");
        using (var stream = new FileStream(database, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            stream.WriteByte((byte)'X');
        }
        var report = await workspace.ReportAsync();
        report.AssertFailed("evidence-or-internal-failure", 5);
    }
}
