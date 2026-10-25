using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

internal static class Stage1Fixtures
{
    public const string BothModels = "fake-alpha,fake-beta";
    public const string AllContracts = "chat.baseline@3,tools.single@3,chat.streaming-integrity@2";

    public static string Inventory => Read("inventory.json");
    public static string Policy => Read("policy.json");
    public static string CanonicalVectors => Read("canonical-vectors.json");

    public static string Read(string fileName)
    {
        var assembly = typeof(Stage1Fixtures).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Stage1Fixtures." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    public static string Mutate(string json, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        mutate(root);
        return root.ToJsonString();
    }

    public static string MutateModel(string json, string modelId, Action<JsonObject> mutate) =>
        Mutate(json, root => mutate(Entry(root, modelId)));

    public static JsonObject Entry(JsonObject root, string modelId) =>
        root["entries"]!.AsArray().Select(item => item!.AsObject())
            .Single(entry => entry["id"]!.GetValue<string>() == modelId);

    public static JsonObject Exclusion(string contract, string reviewExpires) => new()
    {
        ["contract"] = contract,
        ["reasonCode"] = "review-pending",
        ["sourceRef"] = "synthetic-review-1",
        ["ownerRef"] = "synthetic-owner",
        ["reviewExpires"] = reviewExpires
    };
}

internal sealed class Stage1CommandResult : IDisposable
{
    private readonly JsonDocument _summary;

    public Stage1CommandResult(int exitCode, string standardOutput, string standardError, OfflineBoundary boundary)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        Boundary = boundary;
        _summary = JsonDocument.Parse(StandardOutput.Trim());
    }

    public int ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public OfflineBoundary Boundary { get; }
    public JsonElement Summary => _summary.RootElement;
    public string Code => Summary.GetProperty("code").GetString()!;

    public void AssertRejected(string code)
    {
        Assert.Equal(2, ExitCode);
        Assert.Equal(code, Code);
        Assert.Equal(2, Summary.GetProperty("exitCode").GetInt32());
        Assert.Equal(code, StandardError.Trim());
        Assert.False(Summary.GetProperty("commandCompleted").GetBoolean());
        Assert.False(Summary.GetProperty("qualificationPerformed").GetBoolean());
        Assert.False(Summary.GetProperty("executionAuthorized").GetBoolean());
        // A rejected command must never have touched secrets, HTTP or providers.
        AssertNoCredentialOrNetworkActivity();
    }

    public void AssertNoCredentialOrNetworkActivity()
    {
        Assert.Equal(0, Boundary.SecretReads);
        Assert.Equal(0, Boundary.HttpSends);
        Assert.Equal(0, Boundary.HttpClients);
        Assert.Equal(0, Boundary.ProviderConstructions);
    }

    public void Dispose() => _summary.Dispose();
}

internal sealed class Stage1Workspace : IDisposable
{
    private readonly string _root;

    public Stage1Workspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "baize-stage1-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;
    public string OutDir => Out("out");

    public string Out(string name) => Path.Combine(_root, name);

    public string WriteInput(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    public Stage1CommandResult Validate(string inventoryJson, string? inventoryFile = null)
    {
        var inventory = WriteInput(inventoryFile ?? $"inventory-{Guid.NewGuid():N}.json", inventoryJson);
        return Run(new OfflineBoundary(), "inventory", "validate", "--inventory", inventory);
    }

    public Stage1CommandResult Plan(string inventoryJson, string policyJson, string models, string contracts,
        string? outDir = null, OfflineBoundary? boundary = null)
    {
        var inventory = WriteInput($"inventory-{Guid.NewGuid():N}.json", inventoryJson);
        var policy = WriteInput($"policy-{Guid.NewGuid():N}.json", policyJson);
        return Run(boundary ?? new OfflineBoundary(), "plan", "--inventory", inventory, "--policy", policy,
            "--models", models, "--contracts", contracts, "--out", outDir ?? OutDir);
    }

    public static Stage1CommandResult Run(OfflineBoundary boundary, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = QualificationCli.Execute(args, output, error, boundary);
        return new Stage1CommandResult(exitCode, output.ToString(), error.ToString(), boundary);
    }

    public byte[] ReadPlanBytes(string? outDir = null) =>
        File.ReadAllBytes(Path.Combine(outDir ?? OutDir, "plan.json"));

    public byte[] ReadIdentityBytes(string? outDir = null) =>
        File.ReadAllBytes(Path.Combine(outDir ?? OutDir, "identity.json"));

    public JsonElement ReadPlan(string? outDir = null)
    {
        using var document = JsonDocument.Parse(ReadPlanBytes(outDir));
        return document.RootElement.Clone();
    }

    public JsonElement ReadIdentity(string? outDir = null)
    {
        using var document = JsonDocument.Parse(ReadIdentityBytes(outDir));
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class Stage1PlanAssertions
{
    public static JsonElement Subject(JsonElement plan, string modelId) =>
        plan.GetProperty("subjects").EnumerateArray()
            .Single(subject => subject.GetProperty("modelId").GetString() == modelId);

    public static JsonElement Case(JsonElement plan, string modelId, string scenario) =>
        plan.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("modelId").GetString() == modelId
                && item.GetProperty("scenario").GetString() == scenario);

    public static string[] Blockers(JsonElement item) =>
        item.GetProperty("liveEligibilityBlockers").EnumerateArray()
            .Select(blocker => blocker.GetString()!).ToArray();

    public static string[] Observations(JsonElement planCase) =>
        planCase.GetProperty("observations").EnumerateArray()
            .Select(contract => contract.GetProperty("id").GetString()!).ToArray();
}
