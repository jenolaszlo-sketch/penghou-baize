using System.Text.Json;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Execution;

// Loads a frozen Stage 1 plan and verifies every binding the execution path
// depends on: the identity file's plan hash, the current build closure and
// source revision, and the current contract registry hash. Any change fails
// closed before a run directory, secret reference or invocation exists.
internal sealed class FrozenRunPlan
{
    public required string PlanFile { get; init; }
    public required byte[] PlanBytes { get; init; }
    public required string PlanHash { get; init; }
    public required JsonElement Document { get; init; }
    public required string ContractRegistryHash { get; init; }
    public required string SourceRevision { get; init; }
    public required string Framework { get; init; }
    public required string[] Models { get; init; }
    public required JsonElement Policy { get; init; }
    public required JsonElement Inventory { get; init; }
    public required JsonElement[] Subjects { get; init; }
    public required JsonElement[] Cases { get; init; }

    public static FrozenRunPlan Load(string planFile)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(planFile))
            ?? throw new ExecutionException("invalid-plan-path");
        var identityFile = Path.Combine(directory, "identity.json");
        var planBytes = File.ReadAllBytes(planFile);
        if (planBytes.Length > 16 << 20) throw new ExecutionException("plan-too-large");
        var identityBytes = File.Exists(identityFile) ? File.ReadAllBytes(identityFile) : throw new ExecutionException("plan-identity-missing");
        var identity = JsonDocument.Parse(identityBytes).RootElement;
        if (identity.GetProperty("kind").GetString() != "qualification-plan-identity")
            throw new ExecutionException("invalid-plan-identity");
        var planHash = CanonicalJson.Hash(planBytes);
        if (!string.Equals(identity.GetProperty("planHash").GetString(), planHash, StringComparison.Ordinal))
            throw new ExecutionException("plan-identity-mismatch");
        if (identity.GetProperty("executionAuthorized").GetBoolean())
            throw new ExecutionException("plan-claims-execution-authority");
        var document = JsonDocument.Parse(planBytes).RootElement;
        if (document.GetProperty("kind").GetString() != "qualification-plan")
            throw new ExecutionException("invalid-plan");
        if (document.GetProperty("qualificationPerformed").GetBoolean())
            throw new ExecutionException("plan-claims-qualification");
        var source = document.GetProperty("source");
        var registryHash = source.GetProperty("contractRegistryHash").GetString();
        var currentRegistry = CanonicalJson.Identity(ContractRegistry.Current);
        if (!string.Equals(registryHash, currentRegistry, StringComparison.Ordinal))
            throw new ExecutionException("contract-registry-changed");
        var build = source.GetProperty("build");
        var currentBuild = BuildBinding.Collect();
        VerifyBuild(build, currentBuild);
        var policy = document.GetProperty("policy");
        return new FrozenRunPlan
        {
            PlanFile = planFile,
            PlanBytes = planBytes,
            PlanHash = planHash,
            Document = document,
            ContractRegistryHash = currentRegistry,
            SourceRevision = build.GetProperty("sourceRevision").GetString()!,
            Framework = build.GetProperty("framework").GetString()!,
            Models = document.GetProperty("selectedModels").EnumerateArray().Select(m => m.GetString()!).ToArray(),
            Policy = policy,
            Inventory = document.GetProperty("inventory"),
            Subjects = document.GetProperty("subjects").EnumerateArray().ToArray(),
            Cases = document.GetProperty("cases").EnumerateArray().ToArray()
        };
    }

    private static void VerifyBuild(JsonElement recorded, JsonElement current)
    {
        if (!string.Equals(recorded.GetProperty("sourceRevision").GetString(),
                current.GetProperty("sourceRevision").GetString(), StringComparison.Ordinal))
            throw new ExecutionException("source-revision-changed");
        var recordedAssemblies = recorded.GetProperty("assemblies").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString()!, a => a, StringComparer.Ordinal);
        var currentAssemblies = current.GetProperty("assemblies").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString()!, a => a, StringComparer.Ordinal);
        if (!recordedAssemblies.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                currentAssemblies.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ExecutionException("build-closure-changed");
        foreach (var name in recordedAssemblies.Keys)
        {
            var recordedAssembly = recordedAssemblies[name];
            var currentAssembly = currentAssemblies[name];
            if (recordedAssembly.GetProperty("version").GetString() != currentAssembly.GetProperty("version").GetString()
                || recordedAssembly.GetProperty("contentHash").GetString() != currentAssembly.GetProperty("contentHash").GetString())
                throw new ExecutionException("build-closure-changed");
        }
    }
}
