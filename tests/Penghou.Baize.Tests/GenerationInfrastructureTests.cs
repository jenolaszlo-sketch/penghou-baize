using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Tests;

/// <summary>
/// Exercises generation executor infrastructure: non-throwing capability
/// probing, wait-by-handle resume, eager endpoint registration, and the shared
/// transport registration.
/// </summary>
public sealed class GenerationInfrastructureTests
{
    private static GenerationCapabilities TextToImageCapabilities => new()
    {
        Features = GenerationFeature.TextToImage
    };

    private static ImageGenerationRequest ValidRequest =>
        new() { Prompt = "a lighthouse" };

    // ---------- TryValidate ----------

    [Fact]
    public void TryValidate_AcceptedRequest_ReturnsTrueWithNoDiagnostics()
    {
        var accepted = GenerationRequestValidator.TryValidate(
            TextToImageCapabilities,
            ValidRequest,
            out var diagnostics);

        accepted.Should().BeTrue();
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void TryValidate_RejectedRequest_ReturnsFalseWithoutThrowing()
    {
        var accepted = GenerationRequestValidator.TryValidate(
            new GenerationCapabilities { Features = GenerationFeature.None },
            ValidRequest,
            out var diagnostics);

        accepted.Should().BeFalse();
        diagnostics.Should().ContainSingle()
            .Which.Should().Contain("does not support 'TextToImage'");
    }

    // ---------- WaitAsync (resume by handle) ----------

    [Fact]
    public async Task ExecutorWaitAsync_ResumesByHandleWithoutResubmitting()
    {
        var client = new ScriptedPollClient
        {
            PollScript =
            [
                Running(0.5),
                Succeeded()
            ]
        };
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var executor = new GenerationExecutor(
            registry,
            options: Options.Create(new GenerationExecutorOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                InitialPollingInterval = TimeSpan.FromMilliseconds(1)
            }));
        var handle = new GenerationOperationHandle(
            "OpenAi", "image-endpoint", "op-77", "image-model");

        var result = await executor.WaitAsync(
            handle,
            cancellationToken: TestContext.Current.CancellationToken);

        client.SubmitCount.Should().Be(0);
        client.GetCount.Should().Be(2);
        result.Assets.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchExecutorWaitAsync_ResumesChunkByHandle()
    {
        var client = new ScriptedPollClient
        {
            PollScript = [Succeeded()]
        };
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "image-endpoint", client);
        var batchExecutor = new GenerationBatchExecutor(registry);

        var handle = new GenerationOperationHandle(
            "OpenAi", "image-endpoint", "chunk-3", "image-model");

        var result = await batchExecutor.WaitAsync(
            handle,
            cancellationToken: TestContext.Current.CancellationToken);

        client.SubmitCount.Should().Be(0);
        result.Assets.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecutorWaitAsync_UnknownHandle_ThrowsInvalidRequest()
    {
        var registry = new DefaultGenerationClientRegistry();
        registry.Register("OpenAi", "other-endpoint", new ScriptedPollClient());
        var executor = new GenerationExecutor(registry);

        var action = () => executor.WaitAsync(
            new GenerationOperationHandle("OpenAi", "missing-endpoint", "op-1"),
            cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await action.Should().ThrowAsync<BaizeException>()).Which;
        exception.ErrorKind.Should().Be(GenerationErrorKind.InvalidRequest);
        exception.Message.Should().Contain("missing-endpoint");
    }

    // ---------- eager endpoint registration ----------

    [Fact]
    public void RegistryResolution_MaterializesDescriptorsEagerly()
    {
        var services = new ServiceCollection();
        services.AddBaizeGeneration();

        var registeredFromDescriptor = false;
        services.AddSingleton<IGenerationEndpointDescriptor>(
            new DelegateGenerationEndpointDescriptor((_, registry) =>
            {
                registeredFromDescriptor = true;
                registry.Register(
                    "OpenAi",
                    "descriptor-endpoint",
                    new ScriptedPollClient());
            }));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IGenerationClientRegistry>();

        registeredFromDescriptor.Should().BeTrue();
        registry.Endpoints.Should().Contain(endpoint =>
            endpoint.EndpointId == "descriptor-endpoint");
    }

    [Fact]
    public async Task KeyedClient_AndRegistry_ShareTheSameInstance()
    {
        var services = new ServiceCollection();
        services.AddBaizeGeneration();

        var shared = new ScriptedPollClient();
        services.AddSingleton<IGenerationEndpointDescriptor>(
            new DelegateGenerationEndpointDescriptor((_, registry) =>
                registry.Register("OpenAi", "shared-endpoint", shared)));
        services.AddKeyedSingleton<IGenerationClient>("shared-endpoint", (sp, _) =>
            sp.GetRequiredService<IGenerationClientRegistry>().Endpoints.Single().Client);

        using var provider = services.BuildServiceProvider();
        var keyed = provider.GetRequiredKeyedService<IGenerationClient>("shared-endpoint");
        var fromRegistry = provider.GetRequiredService<IGenerationClientRegistry>()
            .Endpoints.Single().Client;

        keyed.Should().BeSameAs(fromRegistry);
        await Task.CompletedTask;
    }

    // ---------- transport registration ----------

    [Fact]
    public void AddBaizeTransport_RegistersNamedClientOnce()
    {
        var services = new ServiceCollection();

        services.AddBaizeTransport();
        services.AddBaizeTransport(); // second call must be a no-op.

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("llm");

        client.Timeout.Should().BePositive();
    }

    private static GenerationOperation Running(double progress) =>
        new(Handle(), GenerationOperationState.Running, Progress: progress);

    private static GenerationOperation Succeeded() =>
        new(
            Handle(),
            GenerationOperationState.Succeeded,
            new GenerationResult(
                [new GeneratedAsset(new InlineGeneratedAssetSource(new byte[] { 1 }, "image/png"))]));

    private static GenerationOperationHandle Handle() =>
        new("OpenAi", "image-endpoint", "op-1", "image-model");

    private sealed class ScriptedPollClient : IGenerationClient
    {
        public GenerationCapabilities Capabilities { get; } =
            new() { Features = GenerationFeature.TextToImage | GenerationFeature.OperationRetrieval };

        public int SubmitCount { get; private set; }

        public int GetCount { get; private set; }

        public List<GenerationOperation> PollScript { get; set; } = [];

        public Task<GenerationOperation> SubmitAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            SubmitCount++;
            return Task.FromResult(new GenerationOperation(
                Handle(),
                GenerationOperationState.Succeeded,
                new GenerationResult([])));
        }

        public Task<GenerationOperation> GetAsync(
            GenerationOperationHandle handle,
            CancellationToken cancellationToken = default)
        {
            var index = GetCount;
            GetCount++;
            return Task.FromResult(
                PollScript[Math.Min(index, PollScript.Count - 1)]);
        }

        public Task<GenerationOperation> CancelAsync(
            GenerationOperationHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerationOperation(handle, GenerationOperationState.Canceled));
    }
}
