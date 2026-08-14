using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize;
using Penghou.Baize.Generation;

// Best-of-N composition: generate N candidates through the logical batch
// executor, score them with an application-level selection policy, and keep the
// best. Selection policy lives here, in the application, never in a provider
// client. This sample uses a deterministic in-process client so it runs with no
// network and no API key; swap in AddBaizeOpenAiGeneration /
// AddBaizeRunwayGeneration and the same composition works against real providers.

var services = new ServiceCollection();
services.AddBaizeGeneration(options => options.Timeout = TimeSpan.FromSeconds(30));

await using var provider = services.BuildServiceProvider();
var registry = provider.GetRequiredService<IGenerationClientRegistry>();
registry.Register("Sample", "local", new ScriptedImageClient(
    new GenerationCapabilities
    {
        Features = GenerationFeature.TextToImage |
                   GenerationFeature.MultipleCandidates |
                   GenerationFeature.OperationRetrieval,
        MaximumCandidates = 4,
        InputTransports = new HashSet<LlmContentTransport>()
    }));

var batchExecutor = provider.GetRequiredService<IGenerationBatchExecutor>();

// Generate 12 candidate images; the endpoint accepts at most 4 per submission,
// so the batch runs 3 concurrent chunks that reuse native candidate counts.
var batch = await batchExecutor.ExecuteAsync(new GenerationBatchRequest(
    new ImageGenerationRequest { Prompt = "a simple flat logo for a tea brand" },
    TotalCount: 12));

Console.WriteLine($"Candidates: {batch.Assets.Count} succeeded, {batch.FailedCount} failed.");

// Application-level selection policy: pick the candidate with the best score.
var selected = SelectBest(batch.Assets);
Console.WriteLine($"Selected candidate {selected.Item1} with score {selected.Item2}.");

static (int, double) SelectBest(IReadOnlyList<GeneratedAsset> assets)
{
    var best = -1;
    var bestScore = double.MinValue;
    for (var index = 0; index < assets.Count; index++)
    {
        // Score by an app-specific heuristic; an evaluator LLM would fit here.
        var score = Score(assets[index]);
        if (score > bestScore)
        {
            best = index;
            bestScore = score;
        }
    }

    return (best, bestScore);
}

// Deterministic stand-in for an evaluator. A real implementation would ask an
// LLM to judge prompt adherence, composition, or brand fit.
static double Score(GeneratedAsset asset)
{
    var metadata = asset.Metadata;
    var size = asset.Size ?? 0;
    var tag = metadata?.TryGetValue("tag", out var value) == true ? value?.ToString() : null;
    var tagBonus = tag == "premium" ? 10.0 : tag == "clean" ? 5.0 : 0.0;
    return size / 1000.0 + tagBonus;
}

/// <summary>
/// An in-process deterministic image client that assigns a plausible size and a
/// rotating tag to every asset so the selection policy is exercised. Keeps the
/// sample dependency-free; a real client talks to a provider.
/// </summary>
internal sealed class ScriptedImageClient(GenerationCapabilities capabilities)
    : IGenerationClient
{
    private static readonly string[] Tags = ["clean", "busy", "premium", "minimal"];
    private int _sequence;

    public GenerationCapabilities Capabilities { get; } = capabilities;

    public Task<GenerationOperation> SubmitAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var count = request is ImageGenerationRequest image ? image.Count : 1;
        var assets = Enumerable.Range(0, count)
            .Select(_ =>
            {
                var index = Interlocked.Increment(ref _sequence) - 1;
                return new GeneratedAsset(
                    new InlineGeneratedAssetSource(new byte[] { (byte)index }, "image/png"),
                    ContentType: "image/png",
                    Size: 1000 + (index * 137) % 900,
                    Metadata: new Dictionary<string, object?> { ["tag"] = Tags[index % Tags.Length] });
            })
            .ToArray();

        var handle = new GenerationOperationHandle(
            "Sample", "local", $"op-{Guid.NewGuid():N}", "sample-image");
        return Task.FromResult(new GenerationOperation(
            handle,
            GenerationOperationState.Succeeded,
            new GenerationResult(assets)));
    }

    public Task<GenerationOperation> GetAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Immediate client: nothing to poll.");

    public Task<GenerationOperation> CancelAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Immediate client: nothing to cancel.");
}