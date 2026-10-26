using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

// Admission gates, eligibility vocabulary and budget concurrency.
public sealed class Stage2AdmissionTests
{
    // Synthetic-only execution: a real-network profile is refused at run time.
    [Fact]
    public async Task ReviewedNetworkProfile_RefusesExecution()
    {
        using var workspace = new Stage2Workspace();
        var inventory = Stage2Fixtures.Inventory.Replace("\"transportProfile\": \"SyntheticFake\"",
            "\"transportProfile\": \"ReviewedNetwork\"", StringComparison.Ordinal);
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3", inventory: inventory))
            .AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-alpha", "chat.baseline@3");
        run.AssertFailed("live-execution-requires-run-live-command", 2);
        Assert.False(Directory.Exists(workspace.RunDir));
    }

    // Excluded cases can never execute.
    [Fact]
    public async Task ExcludedCase_RefusesExecution()
    {
        using var workspace = new Stage2Workspace();
        var inventory = Stage2Fixtures.Inventory;
        var exclusion = """
            "exclusions": []
            """;
        var excluded = """
            "exclusions": [
              { "contract": "chat.baseline@3", "reasonCode": "review-pending", "sourceRef": "synthetic-review-1", "ownerRef": "synthetic-owner", "reviewExpires": "2030-01-01" }
            ]
            """;
        Assert.Contains(exclusion, inventory, StringComparison.Ordinal);
        inventory = inventory.Replace(exclusion, excluded, StringComparison.Ordinal);
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3,tools.single@3", inventory: inventory))
            .AssertCompleted("plan-generated", 0);
        var run = await workspace.RunAsync("fake-alpha", "chat.baseline@3");
        run.AssertFailed("excluded-case-cannot-execute", 2);
    }

    // Unimplemented synthetic transports stay explicitly blocked at planning.
    [Fact]
    public async Task OllamaSynthetic_PlansWithExplicitTransportBlocker()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-beta", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var plan = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(workspace.PlanDir, "plan.json"))).RootElement;
        var betaCase = plan.GetProperty("cases").EnumerateArray()
            .Single(caseElement => caseElement.GetProperty("modelId").GetString() == "fake-beta");
        var blockers = betaCase.GetProperty("liveEligibilityBlockers").EnumerateArray()
            .Select(blocker => blocker.GetString()!).ToArray();
        Assert.Contains("synthetic-transport-not-implemented", blockers);
        Assert.DoesNotContain("real-network-execution-not-implemented", blockers);
    }

    // The revised blocker vocabulary replaces the retired custom-store gaps.
    [Fact]
    public async Task SyntheticOpenAi_PlansWithoutRetiredStageGapBlockers()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var plan = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(workspace.PlanDir, "plan.json"))).RootElement;
        var alphaCase = plan.GetProperty("cases").EnumerateArray()
            .Single(caseElement => caseElement.GetProperty("modelId").GetString() == "fake-alpha");
        var blockers = alphaCase.GetProperty("liveEligibilityBlockers").EnumerateArray()
            .Select(blocker => blocker.GetString()!).ToArray();
        Assert.DoesNotContain("stage2-durable-admission-not-implemented", blockers);
        Assert.DoesNotContain("stage3-semantic-admission-not-implemented", blockers);
        Assert.DoesNotContain("stage4-http-admission-not-implemented", blockers);
        Assert.DoesNotContain("stage5-lifecycle-and-settlement-not-implemented", blockers);
        Assert.DoesNotContain("wire-attempt-bound-unproven", blockers);
        Assert.DoesNotContain("synthetic-transport-not-implemented", blockers);
    }

    // Unknown selectors and trials fail before any run directory exists.
    [Fact]
    public async Task UnknownSelectionAndTrial_RefuseExecution()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha,fake-missing", "chat.baseline@3")).AssertFailed("unknown-model", 2);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3", trial: 9)).AssertFailed("trial-out-of-range", 2);
        Assert.False(Directory.Exists(workspace.RunDir));
    }

    // Shared allowances are protected against concurrent over-allocation: two
    // racing reservations over the same ledger admit at most one.
    [Fact]
    public async Task ConcurrentReservations_SingleFinalSlotWins()
    {
        using var workspace = new Stage2Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var selected = new RunCaseSelection(
            plan.Cases[0].GetProperty("id").GetString()!,
            plan.Cases[0].GetProperty("subjectId").GetString()!,
            "fake-alpha", "chat.baseline@3", plan.Cases[0],
            plan.Subjects[0],
            plan.Cases[0].GetProperty("trials")[0].GetProperty("id").GetString()!,
            plan.Cases[0].GetProperty("trials")[0].GetProperty("slot").GetInt32());
        var request = RunCommand.BuildRequest(plan, selected, Guid.NewGuid().ToString("N"));
        var envelope = request.Budget with { ModelCalls = 1, HttpRequests = 1 };
        var tightRequest = request with { Budget = envelope };
        var units = BudgetLedger.UnitsFor(tightRequest);
        var raceDirectory = workspace.Out("race");
        Directory.CreateDirectory(raceDirectory);
        // Initialize every writer before the race; the race is about the budget
        // CAS, not concurrent store creation.
        var ledgers = Enumerable.Range(0, 8)
            .Select(_ => new EvidenceLedger(Path.Combine(raceDirectory, "evidence.db"), ExecutionFaults.None))
            .ToArray();
        try
        {
            foreach (var ledger in ledgers)
                await ledger.GetHeadAsync(TestContext.Current.CancellationToken);
            var tasks = ledgers.Select(ledger => Task.Run(async () =>
                await BudgetLedger.ReserveAsync(ledger, tightRequest, Guid.NewGuid().ToString("N"), units,
                    CancellationToken.None))).ToArray();
            var outcomes = await Task.WhenAll(tasks);
            Assert.Equal(1, outcomes.Count(outcome => outcome.DenialCode is null));
            Assert.Equal(7, outcomes.Count(outcome => outcome.DenialCode is not null));
        }
        finally
        {
            foreach (var ledger in ledgers) await ledger.DisposeAsync();
        }
    }

    // A contract registry change fails the binding check before any secret.
    [Fact]
    public async Task UnknownObservedContract_FailsBinding()
    {
        var registry = ContractRegistry.Find("chat.baseline@3");
        var request = new CaseExecutionRequest("run", "plan", "case", "trial", 1, "subject", "fake-alpha",
            "chat.baseline@3", ["chat.baseline@3", "chat.not-a-contract@9"], registry.FixtureId,
            registry.FixtureHash, registry.AssertionIds.ToArray(), false, true, 1, 1, null,
            new SubjectBindingEvidence("service", "account", "profile", "openai-chat-completions", "v1", null, true,
                "SyntheticFake", TransportProfiles.SyntheticProfileId, TransportProfiles.Describe("SyntheticFake", null).Identity,
                "endpoint", "OpenAi", "synthetic/model-alpha", "https://alpha.invalid/v1", null,
                new Dictionary<string, string> { ["Dialect"] = "Standard" }, "00:02:00", "00:02:00", true,
                "openai-chat-completions", "SYNTHETIC_UNUSED_KEY_SLOT"),
            new BudgetEnvelopeEvidence(12, 12, null, null, null, null, null, null),
            new DeadlinesEvidence("00:02:00", "00:10:00", "00:00:30"),
            new LimitsEvidence(1 << 20, 4 << 20, 64 << 10));
        var exception = Assert.Throws<ExecutionException>(() =>
            BindingCheck.Run(new BindingInput(request, Guid.NewGuid()), CancellationToken.None));
        Assert.Equal("observed-contract-unknown", exception.Code);
    }
}
