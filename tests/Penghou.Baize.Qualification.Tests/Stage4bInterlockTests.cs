using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

#pragma warning disable xUnit1051

namespace Penghou.Baize.Qualification.Tests;

// Stage 4B external-network interlock: offline run is synthetic-only, run-live
// requires an exact operator authorization, and every mismatch fails before a
// run directory, credential read or dispatch.
public sealed class Stage4bInterlockTests
{
    private const string CandidateModel = "openrouter-llama31-8b-smoke";
    private const string CandidateContracts = "chat.baseline@3,chat.streaming-integrity@2";

    [Fact]
    public async Task OfflineRunWithAllowLive_RefusesRealProfileBeforeSecrets()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync(CandidateModel, CandidateContracts,
            inventory: Stage4bFixtures.Inventory, policy: Stage4bFixtures.Policy))
            .AssertCompleted("plan-generated", 0);
        var previous = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "sentinel-stage4b-must-not-be-read");
        try
        {
            var run = await workspace.RunAsync(CandidateModel, CandidateContracts, allowLive: true);
            run.AssertFailed("live-execution-requires-run-live-command", 2);
            Assert.DoesNotContain("sentinel-stage4b-must-not-be-read", run.StandardOutput);
            Assert.DoesNotContain("sentinel-stage4b-must-not-be-read", run.StandardError);
            Assert.False(Directory.Exists(workspace.RunDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", previous);
        }
    }

    [Fact]
    public async Task OfflineRunWithAllowLiveOnSynthetic_RemainsSynthetic()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-alpha", "chat.baseline@3", allowLive: true);
        run.AssertCompleted("run-summary", 0);
        Assert.False(run.Summary.GetProperty("liveAuthorized").GetBoolean());
        Assert.Equal(JsonValueKind.Null, run.Summary.GetProperty("liveAuthorizationRef").ValueKind);
        Assert.True(Directory.Exists(workspace.RunDir));
    }

    [Fact]
    public void LegacyManifestWithoutLiveFields_RemainsReadable()
    {
        using var workspace = new Stage3Workspace();
        var runDirectory = workspace.Out("legacy-run");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "manifest.json"), """
            {
              "schemaVersion": "1.0",
              "runId": "legacy",
              "createdUtc": "2026-10-25T00:00:00+00:00",
              "planHash": "sha256:00",
              "contractRegistryHash": "sha256:00",
              "sourceRevision": "0.3.0-preview.7+legacy",
              "framework": ".NET 10.0.12",
              "zhinuComponent": "Penghou.Zhinu 0.2.0-preview.4",
              "simingComponent": "Penghou.Siming 1.0.0",
              "cases": []
            }
            """);
        var manifest = RunManifest.Read(runDirectory);
        Assert.False(manifest.LiveAuthorized);
        Assert.Null(manifest.LiveAuthorizationRef);
        Assert.Null(manifest.LiveAuthorizationHash);
    }

    [Fact]
    public async Task RunLiveWithoutAuthorization_RefusesBeforeRunDirectory()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var run = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--out", workspace.Out("live-run"));
        run.AssertFailed("live-authorization-required", 2);
        Assert.False(Directory.Exists(workspace.Out("live-run")));
    }

    [Fact]
    public async Task ValidateLive_AcceptsExactProposedCohortOffline()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var selection = Stage4bAuthorizations.Select(plan);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, selection, runDirectory);
        var authorizationFile = Stage4bAuthorizations.Write(workspace, authorization);
        var previous = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "sentinel-stage4b-must-not-be-read");
        try
        {
            var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
                "validate-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
            result.AssertCompleted("live-authorization-validated", 0);
            Assert.False(result.Summary.GetProperty("qualificationPerformed").GetBoolean());
            Assert.False(result.Summary.GetProperty("executionAuthorized").GetBoolean());
            Assert.Equal(plan.PlanHash, result.Summary.GetProperty("planHash").GetString());
            Assert.Equal(TransportProfiles.Http11ExactId, result.Summary.GetProperty("transportProfileId").GetString());
            Assert.Equal("at-most-1-physical-http-request-attempt-per-admitted-invocation",
                LiveEligibility.Assess(plan, selection[0], "run").WireAttemptBound);
            Assert.Equal(1, result.Summary.GetProperty("maximumModelCalls").GetInt64());
            Assert.Equal(1, result.Summary.GetProperty("maximumHttpRequests").GetInt64());
            Assert.Equal(768, result.Summary.GetProperty("maximumTotalTokens").GetInt64());
            Assert.Equal("0.0004", result.Summary.GetProperty("maximumCost").GetString());
            Assert.Equal("USD", result.Summary.GetProperty("currency").GetString());
            Assert.Equal(runDirectory, result.Summary.GetProperty("artifactDestination").GetString());
            Assert.DoesNotContain("sentinel-stage4b-must-not-be-read", result.StandardOutput);
            Assert.DoesNotContain("sentinel-stage4b-must-not-be-read", result.StandardError);
            Assert.False(Directory.Exists(runDirectory));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", previous);
        }
    }

    [Fact]
    public async Task RunLivePlanHashMismatch_Refused()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan), runDirectory);
        var authorizationFile = Stage4bAuthorizations.Write(workspace,
            authorization with { PlanHash = "sha256:" + new string('0', 64) });
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
        result.AssertFailed("live-authorization-plan-mismatch", 2);
        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public async Task RunLiveEndpointMismatch_Refused()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan), runDirectory);
        var authorizationFile = Stage4bAuthorizations.Write(workspace,
            authorization with { ProviderModel = "meta-llama/llama-3.1-8b-instruct-other" });
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
        result.AssertFailed("live-authorization-endpoint-mismatch", 2);
        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public async Task RunLiveProfileIdentityMismatch_Refused()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan), runDirectory);
        var authorizationFile = Stage4bAuthorizations.Write(workspace,
            authorization with { TransportProfileIdentity = "sha256:" + new string('0', 64) });
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
        result.AssertFailed("live-authorization-profile-mismatch", 2);
        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public async Task RunLiveBudgetOrTokenMismatch_Refused()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan), runDirectory);
        var cases = new[]
        {
            authorization with { Budget = authorization.Budget with { ModelCalls = 0 } },
            authorization with { Budget = authorization.Budget with { InputTokens = null } },
            authorization with { Budget = authorization.Budget with { MaximumCost = "0.0001" } }
        };
        foreach (var candidate in cases)
        {
            var authorizationFile = Stage4bAuthorizations.Write(workspace, candidate);
            var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
                "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
            result.AssertFailed("live-authorization-budget-mismatch", 2);
            Assert.False(Directory.Exists(runDirectory));
        }
    }

    [Fact]
    public async Task RunLiveExpiredAuthorization_Refused()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan), runDirectory);
        var authorizationFile = Stage4bAuthorizations.Write(workspace,
            authorization with { ExpiresUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1) });
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
        result.AssertFailed("live-authorization-expired", 2);
        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public async Task RunLiveOutputMismatch_Refused()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan),
            workspace.Out("authorized-run"));
        var authorizationFile = Stage4bAuthorizations.Write(workspace, authorization);
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", workspace.Out("different-run"));
        result.AssertFailed("live-authorization-output-mismatch", 2);
        Assert.False(Directory.Exists(workspace.Out("authorized-run")));
        Assert.False(Directory.Exists(workspace.Out("different-run")));
    }

    [Fact]
    public async Task RunLiveSelectionMismatch_Refused()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan), runDirectory);
        var changedCase = authorization.Cases[0] with { TrialId = "sha256:" + new string('0', 64) };
        var authorizationFile = Stage4bAuthorizations.Write(workspace,
            authorization with { Cases = [changedCase] });
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
        result.AssertFailed("live-authorization-selection-mismatch", 2);
        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public async Task RunLiveLoopbackAuthorization_RefusedByEligibility()
    {
        using var workspace = new Stage3Workspace();
        var loopbackInventory = Stage4bFixtures.Inventory
            .Replace("\"transportProfile\": \"ReviewedNetwork\"", "\"transportProfile\": \"LoopbackDevelopment\"",
                StringComparison.Ordinal)
            .Replace("https://openrouter.ai/api/v1", "http://127.0.0.1:1/v1", StringComparison.Ordinal);
        (await workspace.PlanAsync(CandidateModel, CandidateContracts, inventory: loopbackInventory,
            policy: Stage4bFixtures.Policy)).AssertCompleted("plan-generated", 0);
        var planFile = Path.Combine(workspace.PlanDir, "plan.json");
        var plan = FrozenRunPlan.Load(planFile);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan), runDirectory);
        var authorizationFile = Stage4bAuthorizations.Write(workspace, authorization);
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
        result.AssertFailed("live-execution-not-eligible", 2);
        Assert.Contains("loopback-not-live-eligible", result.Summary.GetProperty("criticalReasons")
            .EnumerateArray().Select(r => r.GetString()!).ToArray());
        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public async Task RunLiveSyntheticAuthorization_Refused()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var planFile = Path.Combine(workspace.PlanDir, "plan.json");
        var plan = FrozenRunPlan.Load(planFile);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, Stage4bAuthorizations.Select(plan), runDirectory);
        var authorizationFile = Stage4bAuthorizations.Write(workspace, authorization);
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "run-live", "--plan", planFile, "--authorization", authorizationFile, "--out", runDirectory);
        result.AssertFailed("live-command-requires-real-profile", 2);
        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public async Task ResumeLiveWithoutAuthorization_Refused()
    {
        using var workspace = new Stage3Workspace();
        var planFile = await PlanCandidateAsync(workspace);
        var plan = FrozenRunPlan.Load(planFile);
        var selection = Stage4bAuthorizations.Select(plan);
        var runDirectory = workspace.Out("live-run");
        var authorization = Stage4bAuthorizations.Create(plan, selection, runDirectory);
        CreateLiveRunDirectory(workspace, plan, selection, runDirectory, authorization);
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(), "resume", "--run", runDirectory);
        result.AssertFailed("live-resume-requires-authorization", 2);
        Assert.False(File.Exists(Path.Combine(runDirectory, "evidence.db")));
    }

    [Fact]
    public async Task ResumeNonLiveWithAuthorization_Refused()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var planFile = Path.Combine(workspace.PlanDir, "plan.json");
        var plan = FrozenRunPlan.Load(planFile);
        var selection = Stage4bAuthorizations.Select(plan);
        var runDirectory = workspace.Out("synthetic-run");
        Directory.CreateDirectory(runDirectory);
        File.Copy(planFile, Path.Combine(runDirectory, "plan.json"));
        File.Copy(Path.Combine(workspace.PlanDir, "identity.json"), Path.Combine(runDirectory, "identity.json"));
        var cases = selection.Select(s => new ManifestCaseBinding(s.CaseId, s.TrialId, s.TrialSlot, s.ModelId,
            s.ScenarioId, s.SubjectId, QualificationWorkflow.StableRunId(plan.PlanHash, s.CaseId, s.TrialId).ToString("N"))).ToArray();
        RunManifest.Write(runDirectory, RunManifest.Create(plan, "run", cases));
        var authorization = Stage4bAuthorizations.Create(plan, selection, runDirectory);
        var authorizationFile = Stage4bAuthorizations.Write(workspace, authorization);
        var result = await Stage3Workspace.ExecuteAsync(new ExecutionFaults(),
            "resume", "--run", runDirectory, "--authorization", authorizationFile);
        result.AssertFailed("authorization-not-applicable", 2);
    }

    private static async Task<string> PlanCandidateAsync(Stage3Workspace workspace)
    {
        (await workspace.PlanAsync(CandidateModel, CandidateContracts,
            inventory: Stage4bFixtures.Inventory, policy: Stage4bFixtures.Policy))
            .AssertCompleted("plan-generated", 0);
        return Path.Combine(workspace.PlanDir, "plan.json");
    }

    private static void CreateLiveRunDirectory(Stage3Workspace workspace, FrozenRunPlan plan,
        RunCaseSelection[] selection, string runDirectory, LiveAuthorizationDocument authorization)
    {
        Directory.CreateDirectory(runDirectory);
        File.Copy(Path.Combine(workspace.PlanDir, "plan.json"), Path.Combine(runDirectory, "plan.json"));
        File.Copy(Path.Combine(workspace.PlanDir, "identity.json"), Path.Combine(runDirectory, "identity.json"));
        var cases = selection.Select(s => new ManifestCaseBinding(s.CaseId, s.TrialId, s.TrialSlot, s.ModelId,
            s.ScenarioId, s.SubjectId, QualificationWorkflow.StableRunId(plan.PlanHash, s.CaseId, s.TrialId).ToString("N"))).ToArray();
        RunManifest.Write(runDirectory, RunManifest.Create(plan, "run", cases,
            authorization.AuthorizationRef, CanonicalJson.Identity(authorization), liveAuthorized: true));
    }
}
