using System.Reflection;
using System.Text.Json;

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
    // Walk the actual local assembly closure, not an invented package-version list.
    // Framework identity is explicit; SDK/runtime reproducibility is separately recorded.
    public static JsonElement Collect()
    {
        var root = typeof(BuildBinding).Assembly;
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
        return CanonicalJson.Element(new
        {
            runner = root.GetName().Name,
            sourceRevision = root.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            assemblies = visited.Values.ToArray()
        });
    }
}
