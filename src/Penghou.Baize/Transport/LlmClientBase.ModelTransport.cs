using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Penghou.Model.Abstractions;

namespace Penghou.Baize;

public abstract partial class LlmClientBase
{
    private string _modelEndpointId = "default";
    private IBaizeModelTransportFactory _modelTransportFactory = PassThroughBaizeModelTransportFactory.Instance;

    /// <summary>Factory used to replace or decorate semantic model calls.</summary>
    public IBaizeModelTransportFactory ModelTransportFactory
    {
        get => _modelTransportFactory;
        set => _modelTransportFactory = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Sets a stable non-secret endpoint identity from the configured dispatch URI.</summary>
    protected void ConfigureTransportEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0)
            throw new ArgumentException("Model endpoint identity requires an absolute URI without user info.", nameof(endpoint));
        var normalized = new UriBuilder(endpoint) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty };
        var identity = normalized.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var byteCount = Encoding.UTF8.GetByteCount(identity);
        _modelEndpointId = byteCount <= 256
            ? identity
            : "sha256-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    /// <summary>Streams model events through the configured semantic model transport.</summary>
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default) =>
        StreamThroughModelTransportAsync(request, cancellationToken);

    /// <summary>Completes through the configured semantic model transport.</summary>
    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = BaizeModelTransport.Snapshot(request);
        var target = CreateModelTarget();
        var invocation = BaizeModelTransport.CreateInvocation(target, ModelOperation.Complete, snapshot);
        var transportRequest = new ModelTransportRequest<LlmRequest>(invocation, snapshot);
        var direct = new DefaultCompletionTransport(this);
        var transport = ModelTransportFactory.Wrap(target, direct)
            ?? throw new InvalidOperationException("The model transport factory returned null.");
        var result = await transport.InvokeAsync(transportRequest, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The model transport returned a null response.");
        BaizeModelTransport.VerifyResponseRequestId(invocation, result.RequestId);

        var response = result.Payload;
        if (response is null)
            throw new LlmClientException("The model transport returned a null response payload.", LlmClientFailureKind.Protocol);
        if (result.Usage is not null)
            response = response with { Usage = BaizeModelTransport.ToLlmUsage(result.Usage) };
        if (result.ProviderRequestId is { } providerRequestId)
        {
            var diagnostics = response.Diagnostics is null
                ? new LlmProviderDiagnostics(Metadata.Provider, ResponseId: providerRequestId)
                : response.Diagnostics with { ResponseId = providerRequestId };
            response = response with { Diagnostics = diagnostics };
        }
        return response;
    }

    private async IAsyncEnumerable<LlmStreamEvent> StreamThroughModelTransportAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = BaizeModelTransport.Snapshot(request);
        var target = CreateModelTarget();
        var invocation = BaizeModelTransport.CreateInvocation(target, ModelOperation.Stream, snapshot);
        var transportRequest = new ModelTransportRequest<LlmRequest>(invocation, snapshot);
        var direct = new DefaultStreamingTransport(this);
        var transport = ModelTransportFactory.WrapStreaming(target, direct)
            ?? throw new InvalidOperationException("The model transport factory returned null.");

        await foreach (var result in transport.StreamAsync(transportRequest, cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (result is null)
                throw new LlmClientException("The model transport returned a null stream event.", LlmClientFailureKind.Protocol);
            BaizeModelTransport.VerifyResponseRequestId(invocation, result.RequestId);
            var value = result.Payload;
            if (value is null)
                throw new LlmClientException("The model transport returned a null stream event payload.", LlmClientFailureKind.Protocol);
            if (result.Usage is not null)
                value = value with { Usage = BaizeModelTransport.ToLlmUsage(result.Usage) };
            if (result.ProviderRequestId is { } providerRequestId)
            {
                var diagnostics = value.Diagnostics is null
                    ? new LlmProviderDiagnostics(Metadata.Provider, ResponseId: providerRequestId)
                    : value.Diagnostics with { ResponseId = providerRequestId };
                value = value with { Diagnostics = diagnostics };
            }
            yield return value;
        }
    }

    private ModelTarget CreateModelTarget() =>
        new(Metadata.Provider, Model, _modelEndpointId);

    private sealed class DefaultStreamingTransport(LlmClientBase client)
        : IStreamingModelTransport<LlmRequest, LlmStreamEvent>
    {
        public async IAsyncEnumerable<ModelTransportResponse<LlmStreamEvent>> StreamAsync(
            ModelTransportRequest<LlmRequest> request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateDefaultRequest(client, request, ModelOperation.Stream);
            var safeRequest = BaizeModelTransport.Snapshot(request.Payload);
            await foreach (var value in client.StreamDirectAsync(
                               safeRequest,
                               request.Invocation.RequestId,
                               cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
            {
                yield return new ModelTransportResponse<LlmStreamEvent>(
                    request.Invocation.RequestId,
                    value,
                    BaizeModelTransport.ToModelUsage(value.Usage),
                    value.Diagnostics?.ResponseId);
            }
        }
    }

    private sealed class DefaultCompletionTransport(LlmClientBase client)
        : IModelTransport<LlmRequest, LlmResponse>
    {
        public async ValueTask<ModelTransportResponse<LlmResponse>> InvokeAsync(
            ModelTransportRequest<LlmRequest> request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDefaultRequest(client, request, ModelOperation.Complete);
            var safeRequest = BaizeModelTransport.Snapshot(request.Payload);
            var response = await LlmStreamingExtensions.CollectAsync(
                    client.StreamDirectAsync(safeRequest, request.Invocation.RequestId, cancellationToken),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new ModelTransportResponse<LlmResponse>(
                request.Invocation.RequestId,
                response,
                BaizeModelTransport.ToModelUsage(response.Usage),
                response.Diagnostics?.ResponseId);
        }
    }

    private static void ValidateDefaultRequest(
        LlmClientBase client,
        ModelTransportRequest<LlmRequest> request,
        ModelOperation expectedOperation)
    {
        var invocation = request.Invocation;
        var target = client.CreateModelTarget();
        if (invocation.Target != target || invocation.Operation != expectedOperation ||
            invocation.Context != request.Payload.ExecutionContext ||
            invocation.UsageIntent != request.Payload.UsageIntent ||
            invocation.Attempt != request.Payload.TransportAttempt)
        {
            throw new LlmClientException(
                "The model transport request is not bound to this client invocation.",
                LlmClientFailureKind.Protocol);
        }
    }
}
