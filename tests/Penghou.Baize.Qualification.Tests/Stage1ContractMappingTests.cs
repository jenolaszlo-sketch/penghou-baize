using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

// BQ-1b Stage 1 acceptance group B: the CLI consumes the accepted BQ-1a
// descriptors, fixtures and dispatch budgets without duplicating them.
public sealed class Stage1ContractMappingTests
{
    [Fact]
    public void RegistryMatchesAcceptedBq1aDescriptors()
    {
        var expected = new[]
        {
            QualificationContractsV3.Baseline,
            QualificationContractsV3.SingleTool,
            QualificationContractsV3.Streaming
        };
        Assert.Equal(3, ContractRegistry.Current.Count);
        Assert.Equal(expected.Select(descriptor => descriptor.Id).Order(StringComparer.Ordinal),
            ContractRegistry.Current.Select(contract => contract.Id).Order(StringComparer.Ordinal));
        foreach (var descriptor in expected)
        {
            var planned = ContractRegistry.Find(descriptor.Id);
            Assert.Equal(descriptor.Id, planned.Id);
            Assert.Equal(descriptor.FixtureId, planned.FixtureId);
            Assert.Equal(descriptor.RequiresNativeTools, planned.RequiresNativeTools);
            Assert.Equal(descriptor.AssertionIds, planned.AssertionIds);
            Assert.Equal(descriptor.MaximumDispatches, planned.MaximumDispatches);
            Assert.Equal(descriptor.MaximumOutputTokens, planned.MaximumOutputTokens);
            Assert.Equal(1, planned.MaximumDispatches);
            Assert.Equal(512, planned.MaximumOutputTokens);
        }
        Assert.Equal("SharedObservation", ContractRegistry.Find("chat.streaming-integrity@2").Mode);
        Assert.Equal("Invocation", ContractRegistry.Find("chat.baseline@3").Mode);
        Assert.Equal("Invocation", ContractRegistry.Find("tools.single@3").Mode);
        // Fixture identity is the digest of the accepted shared request fixture,
        // not a CLI-local copy.
        Assert.Equal(CanonicalJson.Identity(QualificationFixtures.CreateBaselineRequest()),
            ContractRegistry.Find("chat.baseline@3").FixtureHash);
        Assert.Equal(CanonicalJson.Identity(QualificationFixtures.CreateSingleToolRequest()),
            ContractRegistry.Find("tools.single@3").FixtureHash);
        Assert.NotEqual(ContractRegistry.Find("chat.baseline@3").FixtureHash,
            ContractRegistry.Find("tools.single@3").FixtureHash);
    }

    [Fact]
    public void PlanRecordsExactAssertionsFixturesAndBudgets()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@3,tools.single@3,chat.streaming-integrity@2");
        Assert.Equal(0, result.ExitCode);
        var plan = workspace.ReadPlan();

        var baseline = plan.GetProperty("selectedContracts").EnumerateArray()
            .Single(contract => contract.GetProperty("id").GetString() == "chat.baseline@3");
        Assert.Equal("baize-token@1", baseline.GetProperty("fixtureId").GetString());
        Assert.Equal(QualificationContractsV3.Baseline.AssertionIds,
            baseline.GetProperty("assertionIds").EnumerateArray().Select(id => id.GetString()!));
        Assert.Equal(1, baseline.GetProperty("maximumDispatches").GetInt32());
        Assert.Equal(512, baseline.GetProperty("maximumOutputTokens").GetInt32());
        Assert.Equal("Invocation", baseline.GetProperty("mode").GetString());

        var streaming = plan.GetProperty("selectedContracts").EnumerateArray()
            .Single(contract => contract.GetProperty("id").GetString() == "chat.streaming-integrity@2");
        Assert.Equal("observed-stream@1", streaming.GetProperty("fixtureId").GetString());
        Assert.Equal("SharedObservation", streaming.GetProperty("mode").GetString());

        foreach (var planCase in plan.GetProperty("cases").EnumerateArray())
        {
            Assert.Equal(1, planCase.GetProperty("maximumSemanticInvocationsPerSlot").GetInt32());
            Assert.Equal(1, planCase.GetProperty("maximumNeutralHttpCallsPerSlot").GetInt32());
            Assert.Equal(1, planCase.GetProperty("maximumPhysicalWireAttemptsPerSlot").GetInt32());
        }
    }

    [Fact]
    public void HistoricalContractVersionsCannotEnterNewPlansOrInventory()
    {
        foreach (var historical in new[]
                 {
                     "chat.baseline@2", "tools.single@2", "chat.streaming-integrity@1"
                 })
        {
            using var workspace = new Stage1Workspace();
            using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
                "fake-alpha", historical);
            result.AssertRejected("unknown-or-historical-contract");
            Assert.False(Directory.Exists(workspace.OutDir));
        }

        var inventory = Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha", entry =>
            entry["exclusions"] = new System.Text.Json.Nodes.JsonArray(
                Stage1Fixtures.Exclusion("tools.single@2", "2027-01-01")));
        using var excluded = new Stage1Workspace();
        using var exclusionResult = excluded.Validate(inventory);
        exclusionResult.AssertRejected("unknown-or-historical-contract");
    }

    [Fact]
    public void StreamingIntegritySharesParentInvocation()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
        Assert.Equal(0, result.ExitCode);
        var plan = workspace.ReadPlan();

        var cases = plan.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(4, cases.Length);
        Assert.DoesNotContain(cases, item => item.GetProperty("scenario").GetString() == "chat.streaming-integrity@2");
        foreach (var item in cases)
        {
            Assert.Contains("chat.streaming-integrity@2", Stage1PlanAssertions.Observations(item));
        }

        var requirements = plan.GetProperty("requirements");
        Assert.Equal(12, requirements.GetProperty("semanticInvocations").GetInt64());
        Assert.Equal(12, requirements.GetProperty("neutralHttpCalls").GetInt64());
        Assert.Equal(12, requirements.GetProperty("terminalBackendSends").GetInt64());
        Assert.Equal(12, requirements.GetProperty("physicalWireAttempts").GetInt64());
    }

    [Fact]
    public void StreamingCannotRunWithoutAParentScenario()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.streaming-integrity@2");
        result.AssertRejected("streaming-requires-parent-scenario");
        Assert.False(Directory.Exists(workspace.OutDir));
    }

    [Fact]
    public void StreamObservationExclusionIsRecordedWithoutExtraDispatch()
    {
        var inventory = Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha", entry =>
            entry["exclusions"] = new System.Text.Json.Nodes.JsonArray(
                Stage1Fixtures.Exclusion("chat.streaming-integrity@2", "2027-01-01")));
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
        Assert.Equal(0, result.ExitCode);
        var plan = workspace.ReadPlan();

        var alpha = Stage1PlanAssertions.Case(plan, "fake-alpha", "chat.baseline@3");
        Assert.Equal("chat.streaming-integrity@2",
            alpha.GetProperty("streamingExclusion").GetProperty("contract").GetString());
        Assert.DoesNotContain("chat.streaming-integrity@2", Stage1PlanAssertions.Observations(alpha));
        Assert.Equal(1, alpha.GetProperty("maximumSemanticInvocationsPerSlot").GetInt32());
        var beta = Stage1PlanAssertions.Case(plan, "fake-beta", "chat.baseline@3");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, beta.GetProperty("streamingExclusion").ValueKind);
        Assert.Contains("chat.streaming-integrity@2", Stage1PlanAssertions.Observations(beta));
        // Excluding an observation changes no dispatch requirement.
        Assert.Equal(12, plan.GetProperty("requirements").GetProperty("semanticInvocations").GetInt64());
    }

    [Fact]
    public void CaseCountsAndRepetitionSlotsAreDeterministic()
    {
        using var workspace = new Stage1Workspace();
        using var first = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("first"));
        using var second = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("second"));
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        var firstPlan = workspace.ReadPlan(workspace.Out("first"));
        var secondPlan = workspace.ReadPlan(workspace.Out("second"));

        var firstCases = firstPlan.GetProperty("cases").EnumerateArray().ToArray();
        Assert.All(firstCases, planCase =>
        {
            var trials = planCase.GetProperty("trials").EnumerateArray().ToArray();
            Assert.Equal(3, trials.Length);
            Assert.Equal(new[] { 1, 2, 3 }, trials.Select(trial => trial.GetProperty("slot").GetInt32()).ToArray());
            Assert.Equal(3, trials.Select(trial => trial.GetProperty("id").GetString()!).Distinct(StringComparer.Ordinal).Count());
        });
        Assert.Equal(
            firstCases.Select(planCase => planCase.GetProperty("id").GetString()),
            secondPlan.GetProperty("cases").EnumerateArray().Select(planCase => planCase.GetProperty("id").GetString()));

        // A different repetition count changes the planned slots and dispatch total.
        var policy = Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["repeatCount"] = 2);
        using var reduced = workspace.Plan(Stage1Fixtures.Inventory, policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("reduced"));
        Assert.Equal(0, reduced.ExitCode);
        var reducedPlan = workspace.ReadPlan(workspace.Out("reduced"));
        Assert.All(reducedPlan.GetProperty("cases").EnumerateArray(),
            planCase => Assert.Equal(2, planCase.GetProperty("trials").GetArrayLength()));
        Assert.Equal(8, reducedPlan.GetProperty("requirements").GetProperty("semanticInvocations").GetInt64());
    }

    [Fact]
    public void ContractSelectionChangesThePlanAndRegistryIdentity()
    {
        using var workspace = new Stage1Workspace();
        using var baseline = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@3", workspace.Out("baseline"));
        using var tools = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "tools.single@3", workspace.Out("tools"));
        Assert.Equal(0, baseline.ExitCode);
        Assert.Equal(0, tools.ExitCode);
        var baselinePlan = workspace.ReadPlan(workspace.Out("baseline"));
        var toolsPlan = workspace.ReadPlan(workspace.Out("tools"));

        var registryHash = CanonicalJson.Identity(ContractRegistry.Current);
        Assert.Equal(registryHash,
            baselinePlan.GetProperty("source").GetProperty("contractRegistryHash").GetString());
        Assert.Equal(registryHash,
            toolsPlan.GetProperty("source").GetProperty("contractRegistryHash").GetString());
        Assert.NotEqual(
            baseline.Summary.GetProperty("planHash").GetString(),
            tools.Summary.GetProperty("planHash").GetString());
        Assert.Equal(CanonicalJson.Identity(QualificationFixtures.CreateBaselineRequest()),
            baselinePlan.GetProperty("selectedContracts").EnumerateArray()
                .Single().GetProperty("fixtureHash").GetString());
        Assert.Equal(CanonicalJson.Identity(QualificationFixtures.CreateSingleToolRequest()),
            toolsPlan.GetProperty("selectedContracts").EnumerateArray()
                .Single().GetProperty("fixtureHash").GetString());
    }
}
