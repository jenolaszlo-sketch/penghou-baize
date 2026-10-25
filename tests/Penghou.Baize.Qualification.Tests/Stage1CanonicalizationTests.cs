using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

// BQ-1b Stage 1 acceptance group C: one versioned canonical writer produces
// stable bytes and identities; meaningful sequence order is preserved.
public sealed class Stage1CanonicalizationTests
{
    [Fact]
    public void IdenticalInputsProduceByteIdenticalPlans()
    {
        using var workspace = new Stage1Workspace();
        using var first = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("first"));
        using var second = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("second"));
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);

        Assert.Equal(workspace.ReadPlanBytes(workspace.Out("first")), workspace.ReadPlanBytes(workspace.Out("second")));
        Assert.Equal(workspace.ReadIdentityBytes(workspace.Out("first")), workspace.ReadIdentityBytes(workspace.Out("second")));
        Assert.Equal(first.Summary.GetProperty("planHash").GetString(), second.Summary.GetProperty("planHash").GetString());
        Assert.Equal(CanonicalJson.Hash(workspace.ReadPlanBytes(workspace.Out("first"))),
            first.Summary.GetProperty("planHash").GetString());
        Assert.Equal(first.Summary.GetProperty("planHash").GetString(),
            workspace.ReadIdentity(workspace.Out("first")).GetProperty("planHash").GetString());
    }

    [Fact]
    public void ExistingOutputIsNeverOverwritten()
    {
        using var workspace = new Stage1Workspace();
        using var first = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@3");
        Assert.Equal(0, first.ExitCode);
        var before = workspace.ReadPlanBytes();
        using var second = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@3");
        second.AssertRejected("output-already-exists");
        Assert.Equal(before, workspace.ReadPlanBytes());
    }

    [Fact]
    public void EquivalentPermittedOrderingDoesNotChangeIdentity()
    {
        var inventory = Stage1Fixtures.Inventory;
        var reorderedEntries = Stage1Fixtures.Mutate(inventory, root =>
        {
            var entries = root["entries"]!.AsArray();
            var reversed = entries.Reverse().Select(item => item!.DeepClone()).ToArray();
            entries.Clear();
            foreach (var item in reversed) entries.Add(item);
        });
        var reorderedProperties = ReverseProperties(reorderedEntries);
        var boundsFirst = WithBounds(inventory, "chat.baseline@3", "tools.single@3");
        var boundsReversed = WithBounds(inventory, "tools.single@3", "chat.baseline@3");
        var reorderedPolicy = Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root =>
        {
            var allowlist = root["endpointAllowlist"]!.AsArray();
            var reversed = allowlist.Reverse().Select(item => item!.DeepClone()).ToArray();
            allowlist.Clear();
            foreach (var item in reversed) allowlist.Add(item);
        });
        var policyReorderedProperties = ReverseProperties(reorderedPolicy);

        using var workspace = new Stage1Workspace();
        using var baseline = workspace.Plan(inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("baseline"));
        using var entries = workspace.Plan(reorderedEntries, policyReorderedProperties,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("entries"));
        using var properties = workspace.Plan(reorderedProperties, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("properties"));
        using var bounds = workspace.Plan(boundsFirst, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("bounds"));
        using var boundsSwapped = workspace.Plan(boundsReversed, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("bounds-swapped"));
        Assert.All(new[] { entries, properties, bounds, boundsSwapped }, result => Assert.Equal(0, result.ExitCode));

        Assert.Equal(workspace.ReadPlanBytes(workspace.Out("baseline")), workspace.ReadPlanBytes(workspace.Out("entries")));
        Assert.Equal(workspace.ReadPlanBytes(workspace.Out("baseline")), workspace.ReadPlanBytes(workspace.Out("properties")));
        Assert.Equal(workspace.ReadPlanBytes(workspace.Out("bounds")), workspace.ReadPlanBytes(workspace.Out("bounds-swapped")));
        Assert.Equal(baseline.Summary.GetProperty("planHash").GetString(),
            entries.Summary.GetProperty("planHash").GetString());
        Assert.Equal(bounds.Summary.GetProperty("planHash").GetString(),
            boundsSwapped.Summary.GetProperty("planHash").GetString());

        using var validation = workspace.Validate(reorderedEntries);
        Assert.Equal(0, validation.ExitCode);
    }

    [Fact]
    public void SemanticallyMeaningfulOrderingIsPreserved()
    {
        using var vectors = JsonDocument.Parse(Stage1Fixtures.CanonicalVectors);
        var sequenceVector = vectors.RootElement.EnumerateArray()
            .Single(vector => vector.GetProperty("input").TryGetProperty("sequence", out _));
        Assert.Equal("{\"A\":false,\"a\":true,\"sequence\":[\"b\",\"a\"]}",
            Encoding.UTF8.GetString(CanonicalJson.WriteElement(sequenceVector.GetProperty("input"))));

        using var workspace = new Stage1Workspace();
        using var result = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@3");
        Assert.Equal(0, result.ExitCode);
        var baseline = workspace.ReadPlan().GetProperty("selectedContracts").EnumerateArray().Single();
        // Assertion and fixture-sequence order is written exactly as accepted.
        Assert.Equal(QualificationContractsV3.Baseline.AssertionIds,
            baseline.GetProperty("assertionIds").EnumerateArray().Select(id => id.GetString()!));
    }

    [Fact]
    public void RelevantConfigurationChangesAlterTheAppropriateIdentity()
    {
        using var workspace = new Stage1Workspace();
        using var baseline = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            "fake-alpha", "chat.baseline@3", workspace.Out("baseline"));
        Assert.Equal(0, baseline.ExitCode);
        var baselinePlan = workspace.ReadPlan(workspace.Out("baseline"));
        var baselineSubject = SubjectId(baselinePlan, "fake-alpha");
        var baselineHash = baseline.Summary.GetProperty("planHash").GetString();
        var baselinePolicyHash = baselinePlan.GetProperty("source").GetProperty("policyHash").GetString();
        var baselineInventoryHash = baselinePlan.GetProperty("source").GetProperty("inventoryHash").GetString();

        using var providerModel = workspace.Plan(
            Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["endpoint"]!["providerModel"] = "synthetic/model-alpha-v2"),
            Stage1Fixtures.Policy, "fake-alpha", "chat.baseline@3", workspace.Out("provider-model"));
        var providerModelPlan = workspace.ReadPlan(workspace.Out("provider-model"));
        Assert.NotEqual(baselineSubject, SubjectId(providerModelPlan, "fake-alpha"));
        Assert.NotEqual(baselineHash, providerModel.Summary.GetProperty("planHash").GetString());

        using var service = workspace.Plan(
            Stage1Fixtures.MutateModel(Stage1Fixtures.Inventory, "fake-alpha",
                entry => entry["service"] = "synthetic-host-alpha-2"),
            Stage1Fixtures.Policy, "fake-alpha", "chat.baseline@3", workspace.Out("service"));
        Assert.NotEqual(baselineSubject, SubjectId(workspace.ReadPlan(workspace.Out("service")), "fake-alpha"));

        var repeats = Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["repeatCount"] = 2);
        using var repeat = workspace.Plan(Stage1Fixtures.Inventory, repeats,
            "fake-alpha", "chat.baseline@3", workspace.Out("repeat"));
        var repeatPlan = workspace.ReadPlan(workspace.Out("repeat"));
        // Policy changes alter the plan/policy identity but not the model subject.
        Assert.NotEqual(baselinePolicyHash, repeatPlan.GetProperty("source").GetProperty("policyHash").GetString());
        Assert.NotEqual(baselineHash, repeat.Summary.GetProperty("planHash").GetString());
        Assert.Equal(baselineSubject, SubjectId(repeatPlan, "fake-alpha"));

        var asOf = Stage1Fixtures.Mutate(Stage1Fixtures.Policy, root => root["asOfUtc"] = "2026-10-26T00:00:00Z");
        using var later = workspace.Plan(Stage1Fixtures.Inventory, asOf,
            "fake-alpha", "chat.baseline@3", workspace.Out("as-of"));
        Assert.NotEqual(baselineHash, later.Summary.GetProperty("planHash").GetString());

        using var bounded = workspace.Plan(WithBounds(Stage1Fixtures.Inventory, "chat.baseline@3"),
            Stage1Fixtures.Policy, "fake-alpha", "chat.baseline@3", workspace.Out("bounds"));
        var boundedPlan = workspace.ReadPlan(workspace.Out("bounds"));
        Assert.NotEqual(baselineInventoryHash, boundedPlan.GetProperty("source").GetProperty("inventoryHash").GetString());
        Assert.NotEqual(baselineSubject, SubjectId(boundedPlan, "fake-alpha"));
        Assert.NotEqual(baselineHash, bounded.Summary.GetProperty("planHash").GetString());
        Assert.Equal("RequestedBoundsRequireEvidenceAndEnforcement",
            boundedPlan.GetProperty("requirements").GetProperty("reservationStatus").GetString());
    }

    [Fact]
    public void InvariantCultureProducesStableCanonicalOutput()
    {
        using var workspace = new Stage1Workspace();
        using var baseline = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
            Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("baseline"));
        Assert.Equal(0, baseline.ExitCode);

        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            using var localized = workspace.Plan(Stage1Fixtures.Inventory, Stage1Fixtures.Policy,
                Stage1Fixtures.BothModels, Stage1Fixtures.AllContracts, workspace.Out("localized"));
            Assert.Equal(0, localized.ExitCode);
            Assert.Equal(workspace.ReadPlanBytes(workspace.Out("baseline")),
                workspace.ReadPlanBytes(workspace.Out("localized")));
            Assert.Equal(baseline.Summary.GetProperty("planHash").GetString(),
                localized.Summary.GetProperty("planHash").GetString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Fact]
    public void CanonicalVectorsArePreservedAsRegressionFixtures()
    {
        using var document = JsonDocument.Parse(Stage1Fixtures.CanonicalVectors);
        var vectors = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, vectors.Length);
        foreach (var vector in vectors)
        {
            var expected = vector.GetProperty("canonical").GetString()!;
            var actual = Encoding.UTF8.GetString(CanonicalJson.WriteElement(vector.GetProperty("input")));
            Assert.Equal(expected, actual);
        }
    }

    private static string SubjectId(JsonElement plan, string modelId) =>
        Stage1PlanAssertions.Subject(plan, modelId).GetProperty("id").GetString()!;

    private static string WithBounds(string json, params string[] contracts) =>
        Stage1Fixtures.MutateModel(json, "fake-alpha", entry =>
            entry["bounds"] = new JsonArray(contracts.Select(contract => (JsonNode)new JsonObject
            {
                ["contract"] = contract,
                ["inputTokens"] = 10,
                ["outputTokens"] = 512,
                ["reasoningTokens"] = 0,
                ["totalTokens"] = 522,
                ["sourceRef"] = "synthetic-bound-1",
                ["maximumCost"] = null,
                ["currency"] = null
            }).ToArray()));

    private static string ReverseProperties(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var reversed = new JsonObject();
        foreach (var property in root.Reverse())
            reversed[property.Key] = property.Value?.DeepClone();
        return reversed.ToJsonString();
    }
}
