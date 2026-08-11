using System.Runtime.CompilerServices;

namespace Penghou.Baize.Router;

/// <summary>
/// Lazily resolves endpoint credentials and constructs provider clients without
/// blocking dependency-injection or configuration-reload threads.
/// </summary>
internal sealed class DeferredEndpointClients(
    ILlmClientProvider provider,
    Func<Task<LlmClientProviderContext>> contextFactory)
{
    private readonly object _gate = new();
    private Task<LlmClientProviderContext>? _context;
    private Task<ILlmClient>? _chatClient;
    private Task<IBaizeBatchClient>? _batchClient;

    public LlmEndpointCapabilities Capabilities { get; } = provider.DefaultCapabilities;

    public string ProviderId => provider.Key.Value;

    public Task<ILlmClient> GetChatClientAsync(CancellationToken cancellationToken) =>
        AwaitAndResetOnFailureAsync(
            GetOrCreate(ref _chatClient, CreateChatClientAsync),
            task => Reset(ref _chatClient, task),
            cancellationToken);

    public Task<IBaizeBatchClient> GetBatchClientAsync(CancellationToken cancellationToken) =>
        AwaitAndResetOnFailureAsync(
            GetOrCreate(ref _batchClient, CreateBatchClientAsync),
            task => Reset(ref _batchClient, task),
            cancellationToken);

    private async Task<ILlmClient> CreateChatClientAsync() =>
        provider.CreateClient(await GetContextAsync());

    private async Task<IBaizeBatchClient> CreateBatchClientAsync() =>
        provider.CreateBatchClient(await GetContextAsync()) ??
        throw new InvalidOperationException(
            $"Provider '{provider.Key}' declares native batch support but returned no batch client.");

    private Task<LlmClientProviderContext> GetContextAsync() =>
        AwaitAndResetOnFailureAsync(
            GetOrCreate(ref _context, contextFactory),
            task => Reset(ref _context, task),
            CancellationToken.None);

    private Task<T> GetOrCreate<T>(ref Task<T>? field, Func<Task<T>> factory)
    {
        lock (_gate)
            return field ??= factory();
    }

    private void Reset<T>(ref Task<T>? field, Task<T> failed)
    {
        lock (_gate)
        {
            if (ReferenceEquals(field, failed))
                field = null;
        }
    }

    private static async Task<T> AwaitAndResetOnFailureAsync<T>(
        Task<T> task,
        Action<Task<T>> reset,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        catch when (task.IsFaulted || task.IsCanceled)
        {
            reset(task);
            throw;
        }
    }
}

internal sealed class DeferredLlmClient(
    DeferredEndpointClients endpoint,
    LlmEndpointCapabilities capabilities,
    LlmClientMetadata metadata) : ILlmClient, ILlmClientMetadataProvider
{
    public LlmEndpointCapabilities Capabilities { get; } = capabilities;

    public LlmClientMetadata Metadata { get; } = metadata;

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await endpoint.GetChatClientAsync(cancellationToken);
        await foreach (var item in client.StreamAsync(request, cancellationToken))
            yield return item;
    }
}

internal sealed class DeferredBatchClient(
    DeferredEndpointClients endpoint,
    BatchCapabilities capabilities) : IBaizeBatchClient
{
    public string ProviderId => endpoint.ProviderId;

    public BatchCapabilities Capabilities { get; } = capabilities;

    public async Task<ProviderBatchHandle> SubmitAsync(
        IReadOnlyList<BaizeBatchItem> items,
        BatchSubmissionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await (await endpoint.GetBatchClientAsync(cancellationToken))
            .SubmitAsync(items, options, cancellationToken);

    public async Task<ProviderBatchStatus> GetStatusAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default) =>
        await (await endpoint.GetBatchClientAsync(cancellationToken))
            .GetStatusAsync(handle, cancellationToken);

    public async Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default) =>
        await (await endpoint.GetBatchClientAsync(cancellationToken))
            .GetResultsAsync(handle, cancellationToken);

    public async Task CancelAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default) =>
        await (await endpoint.GetBatchClientAsync(cancellationToken))
            .CancelAsync(handle, cancellationToken);
}
