using System.Text.Json;
using Penghou.Baize.Qualification.Execution;

namespace Penghou.Baize.Qualification.Tests;

// Fail-closed live eligibility: unknown values never become permissive
// defaults, eligibility is evidence, and authorization remains a separate
// explicit operator action.
public sealed class Stage3EligibilityTests
{
    // 2a. A real-network profile without --allow-live is refused before any
    // credential resolution or dispatch.
    [Fact]
    public async Task RealProfileWithoutAllowLive_RefusedBeforeSecrets()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("openrouter-llama31-8b", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("openrouter-llama31-8b", "chat.baseline@3");
        run.AssertFailed("real-network-not-authorized", 2);
        Assert.False(Directory.Exists(workspace.RunDir));
    }

    // 2b. With --allow-live, an ineligible real profile is refused with its
    // evidence-bound reasons; no run directory or secret access exists.
    [Fact]
    public async Task IneligibleRealProfile_RefusedWithReasons()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("deepseek-native-chat", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("deepseek-native-chat", "chat.baseline@3", allowLive: true);
        Assert.Equal(2, run.ExitCode);
        Assert.Equal("run-refused", run.Summary.GetProperty("kind").GetString());
        Assert.Equal("live-execution-not-eligible", run.Summary.GetProperty("code").GetString());
        var reasons = run.Summary.GetProperty("criticalReasons").EnumerateArray()
            .Select(reason => reason.GetString()!).ToArray();
        Assert.Contains("allowance-unverified", reasons);
        Assert.Contains("token-reservation-bound-unknown", reasons);
        Assert.Contains("wire-attempt-bound-unproven", reasons);
        Assert.DoesNotContain("cost-reservation-bound-unknown", reasons);
        Assert.False(Directory.Exists(workspace.RunDir));
    }

    // 2c. A fully bound real profile is refused on exactly one honest gap:
    // the unproven runtime-internal wire-attempt bound.
    [Fact]
    public async Task FullyBoundRealProfile_BlockedOnlyByWireBound()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("openrouter-llama31-8b", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("openrouter-llama31-8b", "chat.baseline@3", allowLive: true);
        Assert.Equal(2, run.ExitCode);
        var reasons = run.Summary.GetProperty("criticalReasons").EnumerateArray()
            .Select(reason => reason.GetString()!).ToArray();
        Assert.Equal(["wire-attempt-bound-unproven"], reasons);
        var limitations = run.Summary.GetProperty("limitations").EnumerateArray()
            .Select(reason => reason.GetString()!).ToArray();
        Assert.Contains("protocol-revision-evidence-unverified", limitations);
    }

    // 2d. Loopback profiles require the same explicit authorization and stay
    // blocked on the same honest wire gap.
    [Fact]
    public async Task LoopbackProfile_RequiresAllowLiveAndStaysBlockedOnWireGap()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("loopback-local-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("loopback-local-alpha", "chat.baseline@3")).AssertFailed("real-network-not-authorized", 2);
        var run = await workspace.RunAsync("loopback-local-alpha", "chat.baseline@3", allowLive: true);
        Assert.Equal(2, run.ExitCode);
        var reasons = run.Summary.GetProperty("criticalReasons").EnumerateArray()
            .Select(reason => reason.GetString()!).ToArray();
        Assert.Equal(["wire-attempt-bound-unproven"], reasons);
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
    public async Task GeminiProfile_PlansAndRefusesWithReasons()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("gemini-native-flash", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("gemini-native-flash", "chat.baseline@3", allowLive: true);
        Assert.Equal(2, run.ExitCode);
        var reasons = run.Summary.GetProperty("criticalReasons").EnumerateArray()
            .Select(reason => reason.GetString()!).ToArray();
        Assert.Contains("allowance-unverified", reasons);
        Assert.Contains("token-reservation-bound-unknown", reasons);
        Assert.Contains("wire-attempt-bound-unproven", reasons);
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
