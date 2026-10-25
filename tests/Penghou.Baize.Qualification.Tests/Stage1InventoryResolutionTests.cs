using System.Text.Json;
using System.Text.Json.Nodes;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

// BQ-1b Stage 1 acceptance group A: inventory parsing, resolution and fail-closed
// rejection. Every case uses the reviewed synthetic inventory and executes no
// network or credential operation.
public sealed class Stage1InventoryResolutionTests
{
    [Fact]
    public void TwoSyntheticModelsResolveToSeparateImmutableSubjects()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
        Assert.Equal(0, result.ExitCode);
        result.AssertNoCredentialOrNetworkActivity();
        var plan = workspace.ReadPlan();

        var subjects = plan.GetProperty("subjects").EnumerateArray().ToArray();
        Assert.Equal(2, subjects.Length);
        Assert.Equal(new[] { "fake-alpha", "fake-beta" },
            subjects.Select(subject => subject.GetProperty("modelId").GetString()).ToArray());
        var ids = subjects.Select(subject => subject.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.StartsWith("sha256:", id, StringComparison.Ordinal));
        Assert.Equal("fake-alpha-endpoint", Requested(subjects[0]).GetProperty("endpoint").GetProperty("id").GetString());
        Assert.Equal("fake-beta-endpoint", Requested(subjects[1]).GetProperty("endpoint").GetProperty("id").GetString());
        Assert.Equal(4, plan.GetProperty("cases").GetArrayLength());
    }

    [Fact]
    public void DifferentHostingServicesDoNotShareSubjectIdentity()
    {
        var inventory = Stage1Fixtures.Mutate(Stage1Fixtures.Inventory, root =>
        {
            var entries = root["entries"]!.AsArray();
            var clone = (JsonObject)entries[0]!.DeepClone();
            clone["id"] = "fake-alpha-copy";
            clone["service"] = "synthetic-host-other";
            clone["endpointProfile"] = "fake-alpha-copy-profile";
            clone["accountScopeRef"] = "synthetic-account-other";
            clone["endpoint"]!["id"] = "fake-alpha-copy-endpoint";
            entries.Add(clone);
        });
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(inventory, Stage1Fixtures.Policy,
            "fake-alpha,fake-alpha-copy", "chat.baseline@3");
        Assert.Equal(0, result.ExitCode);
        var plan = workspace.ReadPlan();

        var original = Stage1PlanAssertions.Subject(plan, "fake-alpha");
        var copy = Stage1PlanAssertions.Subject(plan, "fake-alpha-copy");
        Assert.NotEqual(original.GetProperty("id").GetString(), copy.GetProperty("id").GetString());
        Assert.Equal("synthetic-host-alpha", Requested(original).GetProperty("service").GetString());
        Assert.Equal("synthetic-host-other", Requested(copy).GetProperty("service").GetString());
        // The wire destination is identical; hosting-service identity still separates the subjects.
        Assert.Equal(Resolved(original).GetProperty("baseUrl").GetString(),
            Resolved(copy).GetProperty("baseUrl").GetString());
    }

    [Fact]
    public void ExplicitSettingsAndInheritedDefaultsResolveConsistently()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
        Assert.Equal(0, result.ExitCode);
        var plan = workspace.ReadPlan();

        var alpha = Resolved(Stage1PlanAssertions.Subject(plan, "fake-alpha"));
        var beta = Resolved(Stage1PlanAssertions.Subject(plan, "fake-beta"));

        // Declared dialect is recorded explicitly; the other provider defaults
        // are resolved from the accepted adapter registration.
        Assert.Equal("Standard", alpha.GetProperty("settings").GetProperty("Dialect").GetString());
        Assert.True(alpha.GetProperty("capabilities").GetProperty("nativeToolCalling").GetBoolean());
        // fake-beta inherits Ollama's conservative defaults overlaid by the
        // reviewed "synthetic-tools" profile, whose only override is tools.
        Assert.Equal("synthetic-tools", beta.GetProperty("profile").GetString());
        Assert.True(beta.GetProperty("capabilities").GetProperty("nativeToolCalling").GetBoolean());
        Assert.False(beta.GetProperty("capabilities").GetProperty("streamingToolCallArguments").GetBoolean());
        Assert.Equal("https://alpha.invalid", alpha.GetProperty("origin").GetString());
        Assert.Equal(443, alpha.GetProperty("port").GetInt32());
        Assert.Equal("/v1", alpha.GetProperty("basePath").GetString());
        // The planned attempt ceiling is the tighter of the endpoint timeout and
        // the policy; Ollama's default 100-second transport ceiling wins.
        Assert.Equal("00:02:00", alpha.GetProperty("effectiveAttemptCeiling").GetString());
        Assert.Equal("00:01:40", beta.GetProperty("effectiveAttemptCeiling").GetString());
        Assert.Equal("00:01:40", beta.GetProperty("clientRequestTimeout").GetString());
    }

    [Fact]
    public void MutableAliasesAndUnknownRevisionsRemainExplicitlyUnknown()
    {
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
        Assert.Equal(0, result.ExitCode);
        var plan = workspace.ReadPlan();

        var beta = Stage1PlanAssertions.Subject(plan, "fake-beta");
        Assert.Equal(JsonValueKind.Null, Requested(beta).GetProperty("revision").ValueKind);
        Assert.Equal(JsonValueKind.Null, Requested(beta).GetProperty("apiVersion").ValueKind);
        Assert.True(Requested(beta).GetProperty("mutableAlias").GetBoolean());
        var evidence = Resolved(beta).GetProperty("identityEvidence");
        Assert.False(evidence.GetProperty("protocolRevisionEstablished").GetBoolean());
        Assert.Equal("NotObserved", evidence.GetProperty("providerIdentityProvenance").GetString());
        Assert.Equal("Undetermined", evidence.GetProperty("providerIdentityApplicability").GetString());

        var betaBlockers = Stage1PlanAssertions.Blockers(Stage1PlanAssertions.Case(plan, "fake-beta", "chat.baseline@3"));
        Assert.Contains("model-revision-unknown", betaBlockers);
        Assert.Contains("protocol-revision-unknown", betaBlockers);
        var alphaBlockers = Stage1PlanAssertions.Blockers(Stage1PlanAssertions.Case(plan, "fake-alpha", "chat.baseline@3"));
        Assert.Contains("model-revision-unknown", alphaBlockers);
        Assert.Contains("protocol-revision-evidence-unverified", alphaBlockers);
        Assert.DoesNotContain("protocol-revision-unknown", alphaBlockers);
        // The synthetic fake profile is not denied by wire-attempt uncertainty;
        // that restriction applies to real network profiles only.
        Assert.DoesNotContain("wire-attempt-bound-unproven", alphaBlockers);
    }

    [Fact]
    public void InvalidEndpointPoliciesAreRejected()
    {
        var cases = new (string Inventory, string Code)[]
        {
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["baseUrl"] = "https://operator:super-secret-token@alpha.invalid/v1"),
                "unsafe-endpoint"),
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["baseUrl"] = "http://alpha.invalid/v1"),
                "endpoint-requires-https"),
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["baseUrl"] = "https://alpha.example.com/v1"),
                "synthetic-endpoint-must-be-invalid"),
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["baseUrl"] = "https://alpha.invalid/v1?key=secret"),
                "unsafe-endpoint"),
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["baseUrl"] = "not a url"),
                "invalid-endpoint")
        };
        foreach (var (inventory, code) in cases)
        {
            using var workspace = new Stage1Workspace();
            using var result = workspace.Validate(inventory);
            result.AssertRejected(code);
            result.AssertNoCredentialOrNetworkActivity();
            Assert.DoesNotContain("super-secret-token", result.StandardOutput);
            Assert.DoesNotContain("super-secret-token", result.StandardError);
        }
    }

    [Fact]
    public void InvalidExclusionPoliciesAreRejected()
    {
        var expired = Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha", entry =>
            entry["exclusions"] = new JsonArray(Stage1Fixtures.Exclusion("tools.single@3", "2026-01-01")));
        using (var workspace = new Stage1Workspace())
        {
            // Expiry is a policy-relative decision: inventory validation accepts
            // the structure, plan generation rejects the expired exclusion.
            using var validation = workspace.Validate(expired);
            Assert.Equal(0, validation.ExitCode);
            using var result = workspace.Plan(expired, Stage1Fixtures.Policy, "fake-alpha", "tools.single@3");
            result.AssertRejected("expired-exclusion");
        }

        var current = Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha", entry =>
            entry["exclusions"] = new JsonArray(Stage1Fixtures.Exclusion("tools.single@3", "2027-01-01")));
        using (var workspace = new Stage1Workspace())
        {
            // Positive control: an unexpired exclusion plans successfully and is
            // recorded as an explicit applicability decision with zero dispatches.
            using var result = workspace.Plan(current, Stage1Fixtures.Policy, "fake-alpha",
                "chat.baseline@3,tools.single@3");
            Assert.Equal(0, result.ExitCode);
            var excluded = Stage1PlanAssertions.Case(workspace.ReadPlan(), "fake-alpha", "tools.single@3");
            Assert.Equal("Excluded", excluded.GetProperty("applicability").GetString());
            Assert.Equal(0, excluded.GetProperty("maximumSemanticInvocationsPerSlot").GetInt32());
            Assert.Equal("tools.single@3",
                excluded.GetProperty("exclusion").GetProperty("contract").GetString());
        }

        var duplicate = Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha", entry =>
            entry["exclusions"] = new JsonArray(
                Stage1Fixtures.Exclusion("tools.single@3", "2027-01-01"),
                Stage1Fixtures.Exclusion("tools.single@3", "2027-02-01")));
        using (var workspace = new Stage1Workspace())
        {
            using var result = workspace.Validate(duplicate);
            result.AssertRejected("invalid-exclusion");
        }

        var historical = Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha", entry =>
            entry["exclusions"] = new JsonArray(Stage1Fixtures.Exclusion("chat.baseline@2", "2027-01-01")));
        using (var workspace = new Stage1Workspace())
        {
            using var result = workspace.Validate(historical);
            result.AssertRejected("unknown-or-historical-contract");
        }
    }

    [Fact]
    public void MissingOrConflictingIdentitiesFailClosed()
    {
        var cases = new (string Inventory, string Code)[]
        {
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!.AsObject().Remove("id")), "invalid-reference"),
            (Stage1Fixtures.Mutate(Stage1Fixtures.Inventory, root =>
                root["entries"]!.AsArray().Add(root["entries"]!.AsArray()[0]!.DeepClone())), "duplicate-model"),
            (Stage1Fixtures.Mutate(Stage1Fixtures.Inventory, root =>
            {
                var entries = root["entries"]!.AsArray();
                var clone = (JsonObject)entries[0]!.DeepClone();
                clone["id"] = "fake-alpha-copy";
                clone["endpointProfile"] = "fake-alpha-copy-profile";
                entries.Add(clone);
            }), "duplicate-endpoint"),
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["profile"] = "unknown-profile"), "unknown-profile"),
            (Stage1Fixtures.Mutate(Stage1Fixtures.Inventory, root =>
            {
                // Same profile name bound to a different account/service is ambiguous.
                var entries = root["entries"]!.AsArray();
                var clone = (JsonObject)entries[0]!.DeepClone();
                clone["id"] = "fake-alpha-copy";
                clone["accountScopeRef"] = "synthetic-account-other";
                clone["endpoint"]!["id"] = "fake-alpha-copy-endpoint";
                entries.Add(clone);
            }), "conflicting-endpoint-profile")
        };
        foreach (var (inventory, code) in cases)
        {
            using var workspace = new Stage1Workspace();
            using var result = workspace.Validate(inventory);
            result.AssertRejected(code);
        }
    }

    [Fact]
    public void UnsupportedProvidersAndProtocolsFailClosed()
    {
        var cases = new (string Inventory, string Code)[]
        {
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["provider"] = "Mystery"), "unsupported-provider"),
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-beta",
                entry => entry["protocol"] = "openai-chat-completions"), "unsupported-provider-protocol"),
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["settings"] = new JsonObject { ["Temperature"] = "0.5" }),
                "unsupported-setting"),
            (Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["settings"]!["Dialect"] = "1"), "unknown-dialect")
        };
        foreach (var (inventory, code) in cases)
        {
            using var workspace = new Stage1Workspace();
            using var result = workspace.Validate(inventory);
            result.AssertRejected(code);
        }
    }

    [Fact]
    public void UnknownSelectorsAndEmptySelectionsFailSafely()
    {
        using var workspace = new Stage1Workspace();
        using var unknownModel = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha,no-such-model", "chat.baseline@3");
        unknownModel.AssertRejected("unknown-model");
        Assert.False(Directory.Exists(workspace.OutDir));
        unknownModel.AssertNoCredentialOrNetworkActivity();

        using var unknownContract = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@99", workspace.Out("unknown-contract"));
        unknownContract.AssertRejected("unknown-or-historical-contract");
        Assert.False(Directory.Exists(workspace.Out("unknown-contract")));

        using var empty = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            " ", "chat.baseline@3", workspace.Out("empty"));
        empty.AssertRejected("invalid-options");
        Assert.False(Directory.Exists(workspace.Out("empty")));

        using var duplicate = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha,fake-alpha", "chat.baseline@3", workspace.Out("duplicate"));
        duplicate.AssertRejected("empty-or-duplicate-selector");
    }

    [Fact]
    public void AllExcludedPlansCannotPassAsQualification()
    {
        var inventory = Stage1Fixtures.Mutate(Stage1Fixtures.Inventory, root =>
        {
            foreach (var entry in root["entries"]!.AsArray().Select(item => item!.AsObject()))
                entry["exclusions"] = new JsonArray(
                    Stage1Fixtures.Exclusion("chat.baseline@3", "2027-01-01"),
                    Stage1Fixtures.Exclusion("tools.single@3", "2027-01-01"),
                    Stage1Fixtures.Exclusion("chat.streaming-integrity@2", "2027-01-01"));
        });
        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts);
        result.AssertRejected("all-cases-excluded");
        Assert.False(Directory.Exists(workspace.OutDir));
    }

    private static JsonElement Requested(JsonElement subject) => subject.GetProperty("binding").GetProperty("requested");
    private static JsonElement Resolved(JsonElement subject) => subject.GetProperty("binding").GetProperty("resolved");
}
