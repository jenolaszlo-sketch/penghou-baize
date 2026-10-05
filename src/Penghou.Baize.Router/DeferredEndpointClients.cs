using Penghou.Model.Abstractions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Penghou.Baize.Router;

/// <summary>
/// Lazily resolves endpoint credentials and constructs provider clients without
/// blocking dependency-injection or configuration-reload threads.
/// </summary>
internal sealed class DeferredEndpointClients(
    ILlmClientProvider provider,
    string endpointId,
    string model,
    ILogger? logger,
    Func<Task<LlmClientProviderContext>> contextFactory)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
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

    private async Task<ILlmClient> CreateChatClientAsync()
    {
        _logger.LogDebug(
            "Constructing Baize chat client for endpoint {EndpointId}, provider " +
            "{Provider}, model {Model}",
            endpointId,
            provider.Key.Value,
            model);
        try
        {
            var client = provider.CreateClient(await GetContextAsync());
            _logger.LogInformation(
                "Constructed Baize chat client for endpoint {EndpointId}, provider " +
                "{Provider}, model {Model}",
                endpointId,
                provider.Key.Value,
                model);
            return client;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Failed to construct Baize chat client for endpoint {EndpointId}, " +
                "provider {Provider}, model {Model}, error type {ErrorType}",
                endpointId,
                provider.Key.Value,
                model,
                exception.GetType().FullName);
            throw;
        }
    }

    private async Task<IBaizeBatchClient> CreateBatchClientAsync()
    {
        _logger.LogDebug(
            "Constructing Baize batch client for endpoint {EndpointId}, provider " +
            "{Provider}, model {Model}",
            endpointId,
            provider.Key.Value,
            model);
        try
        {
            var client = provider.CreateBatchClient(await GetContextAsync()) ??
                throw new InvalidOperationException(
                    $"Provider '{provider.Key}' declares native batch support but " +
                    "returned no batch client.");
            _logger.LogInformation(
                "Constructed Baize batch client for endpoint {EndpointId}, provider " +
                "{Provider}, model {Model}",
                endpointId,
                provider.Key.Value,
                model);
            return client;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Failed to construct Baize batch client for endpoint {EndpointId}, " +
                "provider {Provider}, model {Model}, error type {ErrorType}",
                endpointId,
                provider.Key.Value,
                model,
                exception.GetType().FullName);
            throw;
        }
    }

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

internal sealed class DeferredLlmClient : ILlmClient, ILlmCompletionClient, ILlmClientMetadataProvider
{
    private readonly ModelTarget _target;
    private readonly IStreamingModelTransport<LlmRequest, LlmStreamEvent> _streams;
    private readonly IModelTransport<LlmRequest, LlmResponse> _completions;

    public DeferredLlmClient(DeferredEndpointClients endpoint, LlmEndpointCapabilities capabilities,
        LlmClientMetadata metadata, IBaizeModelTransportFactory? transportFactory = null)
    {
        Capabilities = capabilities;
        Metadata = metadata;
        _target = new ModelTarget(metadata.Provider, metadata.Model,
            metadata.EndpointId ?? throw new ArgumentException("A routed endpoint requires an identity.", nameof(metadata)));
        var factory = transportFactory ?? PassThroughBaizeModelTransportFactory.Instance;
        _streams = factory.WrapStreaming(_target, new ProviderStream(endpoint, _target))
            ?? throw new InvalidOperationException("The model transport factory returned no stream transport.");
        _completions = factory.Wrap(_target, new ProviderCompletion(endpoint, _target))
            ?? throw new InvalidOperationException("The model transport factory returned no completion transport.");
    }

    public LlmEndpointCapabilities Capabilities { get; }
    public LlmClientMetadata Metadata { get; }

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = BaizeModelTransport.Snapshot(request);
        var invocation = BaizeModelTransport.CreateInvocation(_target, ModelOperation.Stream, payload);
        await foreach (var response in _streams.StreamAsync(new(invocation, payload), cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            BaizeModelTransport.VerifyResponseRequestId(invocation, response.RequestId);
            yield return response.Payload with
            {
                Usage = BaizeModelTransport.ToLlmUsage(response.Usage) ?? response.Payload.Usage,
                Diagnostics = BindDiagnostics(response.Payload.Diagnostics, response.ProviderRequestId)
            };
        }
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = BaizeModelTransport.Snapshot(request);
        var invocation = BaizeModelTransport.CreateInvocation(_target, ModelOperation.Complete, payload);
        var response = await _completions.InvokeAsync(new(invocation, payload), cancellationToken).ConfigureAwait(false);
        BaizeModelTransport.VerifyResponseRequestId(invocation, response.RequestId);
        return response.Payload with
        {
            Usage = BaizeModelTransport.ToLlmUsage(response.Usage) ?? response.Payload.Usage,
            Diagnostics = BindDiagnostics(response.Payload.Diagnostics, response.ProviderRequestId)
        };
    }

    private LlmProviderDiagnostics? BindDiagnostics(LlmProviderDiagnostics? value, string? requestId) =>
        requestId is null ? value : (value ?? new LlmProviderDiagnostics(Metadata.Provider)) with { ResponseId = requestId };

    private static void Validate(ModelTransportRequest<LlmRequest> request, ModelTarget target, ModelOperation operation)
    {
        if (request.Invocation.Target != target || request.Invocation.Operation != operation ||
            request.Invocation.Context != request.Payload.ExecutionContext || request.Invocation.UsageIntent != request.Payload.UsageIntent ||
            request.Invocation.Attempt != request.Payload.TransportAttempt)
            throw new LlmClientException("The model request does not match its configured endpoint or context.", LlmClientFailureKind.InvalidRequest);
    }

    private sealed class ProviderStream(DeferredEndpointClients endpoint, ModelTarget target)
        : IStreamingModelTransport<LlmRequest, LlmStreamEvent>
    {
        public async IAsyncEnumerable<ModelTransportResponse<LlmStreamEvent>> StreamAsync(
            ModelTransportRequest<LlmRequest> request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Validate(request, target, ModelOperation.Stream);
            var payload = BaizeModelTransport.Snapshot(request.Payload);
            var client = await endpoint.GetChatClientAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var item in client.StreamAsync(payload, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return new(request.Invocation.RequestId, item, BaizeModelTransport.ToModelUsage(item.Usage), item.Diagnostics?.ResponseId);
        }
    }

    private sealed class ProviderCompletion(DeferredEndpointClients endpoint, ModelTarget target)
        : IModelTransport<LlmRequest, LlmResponse>
    {
        public async ValueTask<ModelTransportResponse<LlmResponse>> InvokeAsync(
            ModelTransportRequest<LlmRequest> request, CancellationToken cancellationToken = default)
        {
            Validate(request, target, ModelOperation.Complete);
            var payload = BaizeModelTransport.Snapshot(request.Payload);
            var client = await endpoint.GetChatClientAsync(cancellationToken).ConfigureAwait(false);
            var result = await client.CompleteAsync(payload, cancellationToken).ConfigureAwait(false);
            return new(request.Invocation.RequestId, result, BaizeModelTransport.ToModelUsage(result.Usage), result.Diagnostics?.ResponseId);
        }
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
