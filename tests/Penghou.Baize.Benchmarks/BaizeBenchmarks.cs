using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Penghou.Baize.Generation;
using Penghou.Baize.Tools.Schema;

namespace Penghou.Baize.Benchmarks;

[MemoryDiagnoser]
public class StreamAssemblyBenchmarks
{
    private ChunkClient _client = null!;
    private LlmRequest _request = null!;

    [Params(16, 256, 4096)]
    public int Chunks { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _client = new ChunkClient(Chunks);
        _request = new LlmRequest([new LlmMessage("user", "benchmark")]);
    }

    [Benchmark]
    public Task<LlmResponse> AssembleCompletion() => _client.CompleteAsync(_request);

    private sealed class ChunkClient(int chunks) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < chunks; index++)
            {
                await Task.Yield();
                yield return new LlmStreamEvent(Delta: "token ");
            }
            yield return new LlmStreamEvent(FinishReason: "stop");
        }
    }
}

[MemoryDiagnoser]
public class SchemaGenerationBenchmarks
{
    [Benchmark]
    public string GenerateNestedSchema() =>
        JsonSchemaGenerator.GenerateSchemaJson<SchemaPayload>();

    private sealed class SchemaPayload
    {
        public required string Name { get; init; }
        public required IReadOnlyList<SchemaItem> Items { get; init; }
        public Dictionary<string, string>? Metadata { get; init; }
    }

    private sealed class SchemaItem
    {
        public required int Count { get; init; }
        public required string Value { get; init; }
    }
}

[MemoryDiagnoser]
public class GenerationRegistryBenchmarks
{
    private DefaultGenerationClientRegistry _registry = null!;

    [Params(10, 100, 1000)]
    public int Endpoints { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _registry = new DefaultGenerationClientRegistry();
        for (var index = 0; index < Endpoints; index++)
            _registry.Register("bench", $"endpoint-{index:D4}", new NoopClient());
    }

    [Benchmark]
    public IReadOnlyList<GenerationEndpoint> EnumerateEndpoints() => _registry.Endpoints;

    private sealed class NoopClient : IGenerationClient
    {
        public GenerationCapabilities Capabilities { get; } = new()
        {
            Features = GenerationFeature.None
        };
        public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerationOperation> GetAsync(GenerationOperationHandle handle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerationOperation> CancelAsync(GenerationOperationHandle handle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
