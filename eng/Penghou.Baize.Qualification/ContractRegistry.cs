using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;
using Penghou.Baize.Qualification.Execution;

namespace Penghou.Baize.Qualification.Planning;

internal sealed record PlannedContract(string Id, string FixtureId, string FixtureHash, bool RequiresNativeTools,
    IReadOnlyList<string> AssertionIds, int MaximumDispatches, int MaximumOutputTokens, string Mode);

internal static class ContractRegistry
{
    // The only scenario/fixture/assertion authority remains the accepted library.
    public static IReadOnlyList<PlannedContract> Current { get; } = Array.AsReadOnly(new[]
    {
        Bind(QualificationContractsV3.Baseline, QualificationFixtures.CreateBaselineRequest()),
        Bind(QualificationContractsV3.SingleTool, QualificationFixtures.CreateSingleToolRequest()),
        Bind(QualificationContractsV3.Streaming, null)
    });

    private static PlannedContract Bind(ContractDescriptor descriptor, LlmRequest? request) => new(
        descriptor.Id, descriptor.FixtureId,
        CanonicalJson.Identity(request is null ? (object)new { descriptor.FixtureId, mode = "SharedObservation" } : request),
        descriptor.RequiresNativeTools, descriptor.AssertionIds, descriptor.MaximumDispatches,
        descriptor.MaximumOutputTokens, request is null ? "SharedObservation" : "Invocation");

    public static PlannedContract Find(string id) => Current.SingleOrDefault(c => c.Id == id)
        ?? throw new PlanningException("unknown-or-historical-contract");
}

internal static class BuildBinding
{
    private const string ClosureSchemaVersion = "baize-deployment-closure@1";
    private sealed record ClosureFile(string Path, long SizeBytes, string Sha256);

    // Walk the actual local assembly closure, not an invented package-version list.
    // Framework identity is explicit; SDK/runtime reproducibility is separately recorded.
    public static JsonElement Collect()
    {
        var root = typeof(BuildBinding).Assembly;
        var productVersion = root.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? root.GetName().Version?.ToString() ?? "unknown";
        var buildRevision = productVersion.Contains('+')
            ? productVersion[(productVersion.LastIndexOf('+') + 1)..] : productVersion;
        var configuration = root.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(value => value.Key == "BuildConfiguration")?.Value ?? "unknown";
        var visited = new SortedDictionary<string, object>(StringComparer.Ordinal);
        void Visit(Assembly assembly)
        {
            var name = assembly.GetName().Name!;
            if (visited.ContainsKey(name)) return;
            visited.Add(name, new
            {
                name,
                version = assembly.GetName().Version!.ToString(),
                informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                contentHash = CanonicalJson.Hash(File.ReadAllBytes(assembly.Location))
            });
            foreach (var dependency in assembly.GetReferencedAssemblies().OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                // Shared framework files are outside this tool's deployed closure.
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, dependency.Name + ".dll")))
                    Visit(Assembly.Load(dependency));
            }
        }
        Visit(root);
        // Metadata-only registration uses these assemblies even if a future linker
        // changes which members the CLR loads first.
        Visit(typeof(OpenAi.OpenAiClientProvider).Assembly);
        Visit(typeof(Claude.ClaudeClientProvider).Assembly);
        Visit(typeof(Ollama.OllamaClientProvider).Assembly);
        Visit(typeof(Gemini.GeminiClientProvider).Assembly);
        var closure = CollectDeploymentClosure(AppContext.BaseDirectory, buildRevision, productVersion, configuration);
        return CanonicalJson.Element(new
        {
            runner = root.GetName().Name,
            sourceRevision = productVersion,
            buildRevision,
            productVersion,
            configuration,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            assemblies = visited.Values.ToArray(),
            deploymentClosure = closure
        });
    }

    public static void VerifyDeploymentClosure(JsonElement recordedBuild)
        => VerifyDeploymentClosureAt(recordedBuild, AppContext.BaseDirectory);

    internal static void VerifyDeploymentClosureAt(JsonElement recordedBuild, string deploymentRoot,
        string? runnerAssemblyPath = null)
    {
        if (!recordedBuild.TryGetProperty("deploymentClosure", out var recorded))
            throw new ExecutionException("deployment-closure-missing");
        var assembly = typeof(BuildBinding).Assembly;
        var productVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var buildRevision = productVersion.Contains('+')
            ? productVersion[(productVersion.LastIndexOf('+') + 1)..] : productVersion;
        var configuration = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(value => value.Key == "BuildConfiguration")?.Value ?? "unknown";
        var current = CollectDeploymentClosure(deploymentRoot, buildRevision, productVersion, configuration,
            runnerAssemblyPath);
        if (!string.Equals(CanonicalJson.Hash(CanonicalJson.WriteElement(recorded)),
                CanonicalJson.Hash(CanonicalJson.WriteElement(current)), StringComparison.Ordinal))
            throw new ExecutionException("deployment-closure-changed");
    }

    internal static JsonElement CollectDeploymentClosureAt(string deploymentRoot, string? runnerAssemblyPath = null)
    {
        var assembly = typeof(BuildBinding).Assembly;
        var productVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var buildRevision = productVersion.Contains('+')
            ? productVersion[(productVersion.LastIndexOf('+') + 1)..] : productVersion;
        var configuration = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(value => value.Key == "BuildConfiguration")?.Value ?? "unknown";
        return CollectDeploymentClosure(deploymentRoot, buildRevision, productVersion, configuration,
            runnerAssemblyPath);
    }

    private static JsonElement CollectDeploymentClosure(string deploymentRoot, string buildRevision,
        string productVersion, string configuration, string? runnerAssemblyPath = null)
    {
        var root = Path.GetFullPath(deploymentRoot);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new ExecutionException("deployment-closure-invalid");
        var files = new List<ClosureFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ExecutionException("deployment-closure-invalid");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)
                    || !paths.Add(relative))
                    throw new ExecutionException("deployment-closure-invalid");
                var info = new FileInfo(entry);
                files.Add(new ClosureFile(relative, info.Length, CanonicalJson.Hash(File.ReadAllBytes(entry))));
            }
        }
        var ordered = files.OrderBy(value => value.Path, StringComparer.Ordinal).ToArray();
        var assemblyPath = Path.GetFullPath(runnerAssemblyPath ?? typeof(BuildBinding).Assembly.Location);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!assemblyPath.StartsWith(rootPrefix,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !File.Exists(assemblyPath))
            throw new ExecutionException("deployment-closure-invalid");
        var runnerPath = Path.GetRelativePath(root, assemblyPath).Replace('\\', '/');
        var manifest = CanonicalJson.Element(new
        {
            schemaVersion = ClosureSchemaVersion,
            runnerPath,
            buildRevision,
            productVersion,
            configuration,
            targetFramework = AppContext.TargetFrameworkName,
            runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            files = ordered
        });
        using var document = JsonDocument.Parse(CanonicalJson.WriteElement(manifest));
        var digest = CanonicalJson.Hash(CanonicalJson.WriteElement(document.RootElement));
        return CanonicalJson.Element(new
        {
            schemaVersion = ClosureSchemaVersion,
            runnerPath,
            buildRevision,
            productVersion,
            configuration,
            targetFramework = AppContext.TargetFrameworkName,
            runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            files = ordered,
            digest
        });
    }
}
