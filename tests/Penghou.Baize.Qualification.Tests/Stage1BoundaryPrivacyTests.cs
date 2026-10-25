using System.Text;
using System.Text.Json.Nodes;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

// BQ-1b Stage 1 acceptance group D: offline boundaries, privacy and explicit
// non-qualification status. These tests observe the denial ports directly.
public sealed class Stage1BoundaryPrivacyTests
{
    private const string SecretSlot = "SYNTHETIC_UNUSED_KEY_SLOT";

    [Fact]
    public void InventoryValidationPerformsNoSecretReadsOrNetworkActivity()
    {
        var sentinel = "sentinel-secret-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(SecretSlot, sentinel);
        try
        {
            using var workspace = new Stage1Workspace();
            using var result = workspace.Validate(Stage1Fixtures.Inventory);
            Assert.Equal(0, result.ExitCode);
            result.AssertNoCredentialOrNetworkActivity();
            Assert.Equal(0, result.Boundary.SecretReads);
            Assert.DoesNotContain(sentinel, result.StandardOutput);
            Assert.DoesNotContain(sentinel, result.StandardError);
            Assert.True(result.Summary.GetProperty("commandCompleted").GetBoolean());
            Assert.False(result.Summary.GetProperty("qualificationPerformed").GetBoolean());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SecretSlot, null);
        }
    }

    [Fact]
    public void PlanGenerationPerformsNoSecretReadsOrNetworkActivity()
    {
        var sentinel = "sentinel-secret-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(SecretSlot, sentinel);
        try
        {
            using var workspace = new Stage1Workspace();
            using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
                Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
            Assert.Equal(0, result.ExitCode);
            result.AssertNoCredentialOrNetworkActivity();
            Assert.DoesNotContain(sentinel, Encoding.UTF8.GetString(workspace.ReadPlanBytes()));
            Assert.DoesNotContain(sentinel, Encoding.UTF8.GetString(workspace.ReadIdentityBytes()));
            Assert.DoesNotContain(sentinel, result.StandardOutput);
            Assert.DoesNotContain(sentinel, result.StandardError);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SecretSlot, null);
        }
    }

    [Fact]
    public void InvalidInputsProduceNoQualificationSuccessOrArtifacts()
    {
        var unknownSchema = Stage1Fixtures.Mutate(Stage1Fixtures.Inventory,
            root => root["schemaVersion"] = "2.0");
        using (var workspace = new Stage1Workspace())
        {
            using var result = workspace.Validate(unknownSchema);
            result.AssertRejected("unsupported-schema");
        }

        var policyCases = new (string Policy, string Code)[]
        {
            (Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["cleanupGrace"] = "00:00:10"), "invalid-deadlines"),
            (Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["maximumAvailabilityRetries"] = 1), "unsupported-policy-feature"),
            (Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["capturePolicy"] = "Full"), "unsupported-policy-feature"),
            (Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["maximumHttpRequests"] = 0), "nonpositive-limit"),
            (Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["maximumEstimatedCost"] = "0.10"), "incomplete-money"),
            (Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root =>
            {
                root["maximumOutputTokens"] = 10;
                root["maximumTotalTokens"] = 5;
            }), "contradictory-token-limit")
        };
        foreach (var (policy, code) in policyCases)
        {
            using var workspace = new Stage1Workspace();
            using var result = workspace.Plan(Stage1Fixtures.Inventory, policy, "fake-alpha", "chat.baseline@3");
            result.AssertRejected(code);
            Assert.False(Directory.Exists(workspace.OutDir));
        }

        var incompleteBound = Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha", entry =>
            entry["bounds"] = new JsonArray(new JsonObject
            {
                ["contract"] = "chat.baseline@3",
                ["inputTokens"] = 10,
                ["outputTokens"] = 512,
                ["reasoningTokens"] = 0,
                ["totalTokens"] = 522,
                ["sourceRef"] = "synthetic-bound-1",
                ["maximumCost"] = "0.10",
                ["currency"] = null
            }));
        using (var workspace = new Stage1Workspace())
        {
            using var result = workspace.Validate(incompleteBound);
            result.AssertRejected("incomplete-money");
        }

        using (var workspace = new Stage1Workspace())
        {
            using var missingInventory = Stage1Workspace.Run(new OfflineBoundary(),
                "inventory", "validate", "--inventory", workspace.Out("missing.json"));
            missingInventory.AssertRejected("invalid-authoritative-input");
        }
    }

    [Fact]
    public void CredentialAndPrivateFixtureMaterialDoNotLeakIntoArtifacts()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
        Assert.Equal(0, result.ExitCode);
        var plan = Encoding.UTF8.GetString(workspace.ReadPlanBytes());
        var identity = Encoding.UTF8.GetString(workspace.ReadIdentityBytes());

        // Fixture content is represented only by digest; the synthetic prompt
        // and tool schema text stay out of the portable plan.
        Assert.DoesNotContain("BAIZE_OK", plan);
        Assert.DoesNotContain("Reply with the exact token", plan);
        Assert.DoesNotContain("echo_value", plan);
        Assert.DoesNotContain("additionalProperties", plan);
        Assert.Contains("sha256:", plan);
        // The nonsecret secret-slot reference is retained; no workspace path is.
        Assert.Contains(SecretSlot, plan);
        Assert.DoesNotContain(workspace.Root, plan);
        Assert.DoesNotContain("Authorization", plan);
        Assert.DoesNotContain("Authorization", identity);
    }

    [Fact]
    public void MissingEconomicsAreRecordedAsUnknownNotZeroOrFree()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@3");
        Assert.Equal(0, result.ExitCode);
        var plan = workspace.ReadPlan();
        var requirements = plan.GetProperty("requirements");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, requirements.GetProperty("maximumCost").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, requirements.GetProperty("currency").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, requirements.GetProperty("inputTokens").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, requirements.GetProperty("totalTokens").ValueKind);
        var planCase = Stage1PlanAssertions.Case(plan, "fake-alpha", "chat.baseline@3");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, planCase.GetProperty("requestedReservation").ValueKind);
        var blockers = Stage1PlanAssertions.Blockers(planCase);
        Assert.Contains("economics-unknown", blockers);
        Assert.Contains("allowance-unverified", blockers);
        Assert.Contains("token-reservation-bound-unknown", blockers);
        Assert.Contains("cost-reservation-bound-unknown", blockers);

        // A reviewed explicit bound computes planned token requirements while
        // the missing price still blocks as unknown rather than zero.
        var bounded = Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha", entry =>
            entry["bounds"] = new JsonArray(new JsonObject
            {
                ["contract"] = "chat.baseline@3",
                ["inputTokens"] = 10,
                ["outputTokens"] = 512,
                ["reasoningTokens"] = 0,
                ["totalTokens"] = 522,
                ["sourceRef"] = "synthetic-bound-1",
                ["maximumCost"] = null,
                ["currency"] = null
            }));
        using var boundedWorkspace = new Stage1Workspace();
        using var boundedResult = boundedWorkspace.Plan(bounded, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@3");
        Assert.Equal(0, boundedResult.ExitCode);
        var boundedPlan = boundedWorkspace.ReadPlan();
        var boundedRequirements = boundedPlan.GetProperty("requirements");
        Assert.Equal(30, boundedRequirements.GetProperty("inputTokens").GetInt64());
        Assert.Equal(1536, boundedRequirements.GetProperty("outputTokens").GetInt64());
        Assert.Equal(0, boundedRequirements.GetProperty("reasoningTokens").GetInt64());
        Assert.Equal(1566, boundedRequirements.GetProperty("totalTokens").GetInt64());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, boundedRequirements.GetProperty("maximumCost").ValueKind);
        var boundedBlockers = Stage1PlanAssertions.Blockers(
            Stage1PlanAssertions.Case(boundedPlan, "fake-alpha", "chat.baseline@3"));
        Assert.DoesNotContain("token-reservation-bound-unknown", boundedBlockers);
        Assert.Contains("token-bound-enforcement-unverified", boundedBlockers);
        Assert.Contains("cost-reservation-bound-unknown", boundedBlockers);
    }

    [Fact]
    public void PlannedEligibilityCannotBeMistakenForExecutionPermission()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("plan-generated", result.Summary.GetProperty("kind").GetString());
        Assert.True(result.Summary.GetProperty("commandCompleted").GetBoolean());
        Assert.False(result.Summary.GetProperty("qualificationPerformed").GetBoolean());
        Assert.False(result.Summary.GetProperty("executionAuthorized").GetBoolean());
        Assert.Equal("Blocked", result.Summary.GetProperty("liveEligibility").GetString());

        var plan = workspace.ReadPlan();
        Assert.False(plan.GetProperty("executionAuthorized").GetBoolean());
        Assert.False(plan.GetProperty("qualificationPerformed").GetBoolean());
        Assert.Equal("Blocked", plan.GetProperty("liveEligibility").GetString());
        Assert.NotEmpty(plan.GetProperty("liveEligibilityBlockers").EnumerateArray());
        Assert.NotEmpty(plan.GetProperty("requiredEvidence").EnumerateArray());
        Assert.NotEmpty(plan.GetProperty("limitations").EnumerateArray());
        Assert.Equal("PlannedRequirementsNotObservedOrEnforced",
            plan.GetProperty("requirements").GetProperty("quantityStatus").GetString());
        Assert.Equal(CanonicalJson.Version, plan.GetProperty("canonicalWriter").GetString());
        Assert.All(plan.GetProperty("cases").EnumerateArray(), planCase =>
        {
            Assert.Equal("Blocked", planCase.GetProperty("liveEligibility").GetString());
            Assert.NotEmpty(planCase.GetProperty("liveEligibilityBlockers").EnumerateArray());
        });

        var identity = workspace.ReadIdentity();
        Assert.Equal("qualification-plan-identity", identity.GetProperty("kind").GetString());
        Assert.False(identity.GetProperty("executionAuthorized").GetBoolean());
        Assert.Equal(result.Summary.GetProperty("planHash").GetString(), identity.GetProperty("planHash").GetString());
    }

    [Fact]
    public void UnknownSchemaVersionsAndCommandsFailExplicitly()
    {
        using var workspace = new Stage1Workspace();
        foreach (var command in new[] { "run", "resume", "report", "project", "compare" })
        {
            using var unsupported = Stage1Workspace.Run(new OfflineBoundary(), command, "--plan", "plan.json");
            unsupported.AssertRejected("unknown-command");
        }
        using var discover = Stage1Workspace.Run(new OfflineBoundary(),
            "inventory", "discover", "--profile", "x", "--output", "out.json", "--allow-network");
        discover.AssertRejected("unknown-command");
        using var extra = Stage1Workspace.Run(new OfflineBoundary(), "plan",
            "--inventory", "i.json", "--policy", "p.json", "--models", "m", "--contracts", "c",
            "--out", "o", "--allow-live", "now");
        extra.AssertRejected("invalid-options");
        using var empty = Stage1Workspace.Run(new OfflineBoundary());
        empty.AssertRejected("unknown-command");

        var policyVersion = Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["schemaVersion"] = "1.1");
        using var plan = workspace.Plan(Stage1Fixtures.Inventory, policyVersion, "fake-alpha", "chat.baseline@3");
        plan.AssertRejected("unsupported-schema");

        var kind = Stage1Fixtures.Mutate(Stage1Fixtures.Inventory, root => root["kind"] = "other-kind");
        using var validation = workspace.Validate(kind);
        validation.AssertRejected("unsupported-schema");
    }
}
