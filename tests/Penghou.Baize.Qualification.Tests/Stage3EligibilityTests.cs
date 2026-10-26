using System.Text.Json;
using Penghou.Baize.Qualification.Execution;

namespace Penghou.Baize.Qualification.Tests;

// Fail-closed live eligibility: unknown values never become permissive
// defaults, eligibility is evidence, and authorization remains a separate
// explicit operator action.
public sealed class Stage3EligibilityTests
{
    // 2a. A real-network profile is refused by the offline run command even if
    // the legacy --allow-live flag is accidentally supplied. It never reads a
    // credential or enters the live eligibility path.
    [Fact]
    public async Task RealProfileThroughOfflineRun_RefusedBeforeSecrets()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("openrouter-llama31-8b", "chat.baseline@3", inventory: Stage4Fixtures.Inventory)).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("openrouter-llama31-8b", "chat.baseline@3"))
            .AssertFailed("live-execution-requires-run-live-command", 2);
        (await workspace.RunAsync("openrouter-llama31-8b", "chat.baseline@3", allowLive: true))
            .AssertFailed("live-execution-requires-run-live-command", 2);
        Assert.False(Directory.Exists(workspace.RunDir));
    }

    // 2b. An ineligible real profile still reports its evidence-bound reasons
    // in the offline assessment; the offline run command does not enter this
    // path.
    [Fact]
    public async Task IneligibleRealProfile_AssessmentReportsReasons()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("deepseek-native-chat", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var assessment = LiveEligibility.Assess(plan, Select(plan), "run");
        Assert.Equal(LiveEligibility.StateBlocked, assessment.State);
        Assert.Contains("allowance-unverified", assessment.CriticalReasons);
        Assert.Contains("token-reservation-bound-unknown", assessment.CriticalReasons);
        Assert.Contains("transport-profile-unreviewed", assessment.CriticalReasons);
        Assert.Contains("wire-attempt-bound-unproven", assessment.CriticalReasons);
        Assert.DoesNotContain("cost-reservation-bound-unknown", assessment.CriticalReasons);
        Assert.False(Directory.Exists(workspace.RunDir));
    }

    // 2c. A fully bound real profile with the reviewed transport profile is
    // transport-eligible. It still needs the explicit operator authorization
    // for any actual live run; this stage performs none.
    [Fact]
    public async Task FullyBoundRealProfile_IsTransportEligibleButNotAuthorizedHere()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("openrouter-llama31-8b", "chat.baseline@3", inventory: Stage4Fixtures.Inventory)).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var assessment = LiveEligibility.Assess(plan, Select(plan), "run");
        Assert.Equal(LiveEligibility.StateEligible, assessment.State);
        Assert.Empty(assessment.CriticalReasons);
        Assert.Equal(1, assessment.PhysicalAttemptUpperBound);
        Assert.Equal("at-most-1-physical-http-request-attempt-per-admitted-invocation", assessment.WireAttemptBound);
        var limitations = assessment.Limitations;
        Assert.Contains("protocol-revision-evidence-unverified", limitations);
        Assert.False(Directory.Exists(workspace.RunDir));
    }

    // 2d. Loopback profiles are never live qualification targets, and the
    // offline run command cannot reach them even with --allow-live.
    [Fact]
    public async Task LoopbackProfile_OfflineRunRefusedAndAssessmentBlocksLive()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("loopback-local-alpha", "chat.baseline@3", inventory: Stage4Fixtures.Inventory)).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("loopback-local-alpha", "chat.baseline@3", allowLive: true))
            .AssertFailed("live-execution-requires-run-live-command", 2);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var assessment = LiveEligibility.Assess(plan, Select(plan), "run");
        Assert.Equal(LiveEligibility.StateBlocked, assessment.State);
        Assert.Equal(["loopback-not-live-eligible"], assessment.CriticalReasons);
        Assert.False(Directory.Exists(workspace.RunDir));
    }

    // Synthetic runs record a synthetic-only eligibility assessment as
    // evidence; the assessment is recorded but never confers live authority.
    [Fact]
    public async Task SyntheticRun_RecordsSyntheticOnlyAssessment()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        var eligibility = Stage2Evidence.Event(Stage2Evidence.ReadAllDirectory(workspace.RunDir),
            EvidenceLedger.EligibilityAssessed);
        Assert.Equal("SyntheticOnly", eligibility.GetProperty("state").GetString());
        Assert.Equal(0, eligibility.GetProperty("criticalReasons").GetArrayLength());
    }

    // Gemini-native profiles plan and remain blocked for live execution with
    // all missing economics/allowance evidence visible.
    [Fact]
    public async Task GeminiProfile_PlansAndAssessmentReportsReasons()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("gemini-native-flash", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var assessment = LiveEligibility.Assess(plan, Select(plan), "run");
        Assert.Equal(LiveEligibility.StateBlocked, assessment.State);
        Assert.Contains("allowance-unverified", assessment.CriticalReasons);
        Assert.Contains("token-reservation-bound-unknown", assessment.CriticalReasons);
        Assert.Contains("transport-profile-unreviewed", assessment.CriticalReasons);
        Assert.Contains("wire-attempt-bound-unproven", assessment.CriticalReasons);
    }

    private static RunCaseSelection Select(FrozenRunPlan plan)
    {
        var caseElement = plan.Cases[0];
        var subjectId = caseElement.GetProperty("subjectId").GetString()!;
        var subject = plan.Subjects.Single(s => s.GetProperty("id").GetString() == subjectId);
        var trial = caseElement.GetProperty("trials")[0];
        return new RunCaseSelection(caseElement.GetProperty("id").GetString()!, subjectId,
            caseElement.GetProperty("modelId").GetString()!, caseElement.GetProperty("scenario").GetString()!,
            caseElement, subject, trial.GetProperty("id").GetString()!, trial.GetProperty("slot").GetInt32());
    }
}

// Known-defect reproduction and compatibility attribution in evidence.
public sealed class Stage3AttributionTests
{
    // 14. The preserved F1 adapter defect remains reproducible through the
    // guarded composition and is attributed to the adapter, fixture-verified.
    [Fact]
    public async Task F1CoTerminal_RemainsReproducibleAndAttributed()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-coterminal", "tools.single@3,chat.streaming-integrity@2"))
            .AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-coterminal", "tools.single@3,chat.streaming-integrity@2"))
            .AssertCompleted("run-summary", 3);
        var finished = Stage2Evidence.Event(Stage2Evidence.ReadAllDirectory(workspace.RunDir),
            EvidenceLedger.AttemptFinished);
        Assert.Equal("Failed", finished.GetProperty("outcome").GetString());
        var streaming = finished.GetProperty("assertions").EnumerateArray()
            .Single(assertion => assertion.GetProperty("id").GetString() == "stream.sequence");
        Assert.Equal("Failed", streaming.GetProperty("status").GetString());
        Assert.Equal("invalid-event-sequence", streaming.GetProperty("code").GetString());
        Assert.Equal("baize-adapter-defect", finished.GetProperty("suggestedAttributionClass").GetString());
        Assert.Equal("deterministic-fixture", finished.GetProperty("attributionBasis").GetString());
        Assert.Equal("assertion", finished.GetProperty("failureStage").GetString());
        var reportCase = Stage2Evidence.ReportDirectory(workspace.RunDir).GetProperty("report").GetProperty("cases")[0];
        Assert.Equal("baize-adapter-defect", reportCase.GetProperty("suggestedAttributionClass").GetString());
        Assert.Contains("stream.sequence", reportCase.GetProperty("failedAssertionIds").EnumerateArray()
            .Select(id => id.GetString()!).ToArray());
    }

    // 13b. A wrong-output failure stays unverified rather than being blamed
    // on the adapter, provider or model without evidence.
    [Fact]
    public async Task WrongValueFailure_StaysUnverified()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-wrong", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-wrong", "chat.baseline@3")).AssertCompleted("run-summary", 3);
        var finished = Stage2Evidence.Event(Stage2Evidence.ReadAllDirectory(workspace.RunDir),
            EvidenceLedger.AttemptFinished);
        Assert.Equal("Failed", finished.GetProperty("outcome").GetString());
        Assert.Equal("unverified", finished.GetProperty("suggestedAttributionClass").GetString());
        Assert.Equal("observed-normalized-output", finished.GetProperty("attributionBasis").GetString());
        Assert.Equal("contract-assertion-failure", finished.GetProperty("attribution").GetString());
    }

    // 13c. A tool contract failure keeps raw assertion outcomes separate from
    // the (unverified) attribution.
    [Fact]
    public async Task ToolArgumentFailure_PreservesRawAssertions()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-toolbad", "tools.single@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-toolbad", "tools.single@3")).AssertCompleted("run-summary", 3);
        var finished = Stage2Evidence.Event(Stage2Evidence.ReadAllDirectory(workspace.RunDir),
            EvidenceLedger.AttemptFinished);
        var failed = finished.GetProperty("assertions").EnumerateArray()
            .Where(assertion => assertion.GetProperty("status").GetString() == "Failed")
            .Select(assertion => assertion.GetProperty("id").GetString()!).ToArray();
        Assert.Contains("tool.json", failed);
        Assert.Equal("unverified", finished.GetProperty("suggestedAttributionClass").GetString());
    }
}
