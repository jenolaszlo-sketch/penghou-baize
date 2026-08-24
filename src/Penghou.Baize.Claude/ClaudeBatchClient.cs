using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize;

namespace Penghou.Baize.Claude;

/// <summary>
/// <see cref="IBaizeBatchClient"/> implementation for the Anthropic Messages
/// Batch API (<c>POST /v1/messages/batches</c>). Submits the requests inline
/// (no file upload), then polls the batch status and retrieves the results
/// file. The client is stateless: a submitted batch can be resumed purely
/// through its serializable <see cref="ProviderBatchHandle"/>.
/// </summary>
public sealed class ClaudeBatchClient : BaizeBatchClientBase
{
    private const string AnthropicVersion = "2023-06-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Uri _batchesUri;
    private readonly LlmEndpointCapabilities _capabilities;
    private readonly ClaudeThinkingStyle _thinkingStyle;

    /// <summary>Applies the Anthropic credential and transport-version headers.</summary>
    protected override void ApplyAuth(HttpRequestMessage request)
    {
        ApplyCredentialHeader(request, "x-api-key");
        request.Headers.Add("anthropic-version", AnthropicVersion);
    }

    /// <summary>
    /// Creates an Anthropic Messages Batch client.
    /// </summary>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="model">The Anthropic model identifier (for example <c>claude-sonnet-4-5</c>).</param>
    /// <param name="apiKey">The Anthropic API key.</param>
    /// <param name="baseUrl">Base API URL; defaults to <c>https://api.anthropic.com</c>.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="thinkingStyle">The extended-thinking contract the model generation uses.</param>
    public ClaudeBatchClient(
        IHttpClientFactory httpClientFactory,
        string model,
        string apiKey,
        string baseUrl,
        LlmEndpointCapabilities capabilities,
        ClaudeThinkingStyle thinkingStyle = ClaudeThinkingStyle.Adaptive)
        : base("Claude", model, httpClientFactory, apiKey, capabilities)
    {
        _capabilities = capabilities;
        _thinkingStyle = thinkingStyle;
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        _batchesUri = new Uri($"{normalizedBaseUrl}/v1/messages/batches");
    }

    /// <inheritdoc />
    public override async Task<ProviderBatchHandle> SubmitAsync(
        IReadOnlyList<BaizeBatchItem> items,
        BatchSubmissionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        BatchRequestValidator.ValidateItems(items, ProviderId);

        foreach (var item in items)
            ClaudeMessageRequestMapper.Validate(Model, _capabilities, item.Request);

        var createBody = new ClaudeMessageBatchCreateRequest
        {
            Requests = items
                .Select(item => new ClaudeMessageBatchRequestItem
                {
                    CustomId = item.RequestId,
                    Params = ClaudeMessageRequestMapper.Build(
                        Model,
                        _capabilities,
                        _thinkingStyle,
                        item.Request,
                        streaming: false)
                })
                .ToList(),
            Metadata = options?.Metadata
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _batchesUri);

        ApplyAuth(request);

        request.Content = new StringContent(
            JsonSerializer.Serialize(createBody, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var batch = await SendAsync<ClaudeMessageBatch>(
            request,
            JsonOptions,
            cancellationToken);

        if (string.IsNullOrEmpty(batch.Id))
            throw new LlmClientException(
                "Anthropic batch creation returned no batch identifier.",
                LlmClientFailureKind.Protocol);

        return new ProviderBatchHandle(
            ProviderId: ProviderId,
            BatchId: batch.Id!);
    }

    /// <inheritdoc />
    public override async Task<ProviderBatchStatus> GetStatusAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{_batchesUri}/{handle.BatchId}"));

        ApplyAuth(request);

        var batch = await SendAsync<ClaudeMessageBatch>(
            request,
            JsonOptions,
            cancellationToken);

        return new ProviderBatchStatus(
            State: MapState(
                batch.ProcessingStatus,
                batch.RequestCounts),
            ProviderStatus: batch.ProcessingStatus,
            Total: batch.RequestCounts?.Total,
            Completed: batch.RequestCounts?.Succeeded,
            Failed: batch.RequestCounts?.Errored);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{_batchesUri}/{handle.BatchId}/results"));

        ApplyAuth(request);

        var httpClient = CreateTransport();

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new LlmClientException(
                $"Anthropic batch results retrieval failed with HTTP {(int)response.StatusCode}: {content}",
                (int)response.StatusCode);
        }

        var results = new List<BaizeBatchResult>();

        foreach (var rawLine in SplitJsonl(content))
        {
            ClaudeMessageBatchResultLine? line;

            try
            {
                line = JsonSerializer.Deserialize<ClaudeMessageBatchResultLine>(
                    rawLine,
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new LlmClientException(
                    $"Failed to parse Anthropic batch result line: {rawLine}",
                    ex);
            }

            if (line is null || string.IsNullOrWhiteSpace(line.CustomId))
            {
                throw new LlmClientException(
                    $"Anthropic batch result line has no custom_id: {rawLine}",
                    LlmClientFailureKind.Protocol);
            }

            results.Add(NormalizeResult(line));
        }

        return results;
    }

    /// <inheritdoc />
    public override async Task CancelAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        if (!Capabilities.HasFlag(BatchCapabilities.Cancellation))
        {
            throw new NotSupportedException(
                "This Anthropic endpoint does not support batch cancellation.");
        }

        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{_batchesUri}/{handle.BatchId}/cancel"));

        ApplyAuth(request);

        await SendAsync<ClaudeMessageBatch>(request, JsonOptions, cancellationToken);
    }



    private static BaizeBatchResult NormalizeResult(
        ClaudeMessageBatchResultLine line)
    {
        var requestId = line.CustomId!;
        var result = line.Result;
        var type = result?.Type;

        switch (type)
        {
            case "succeeded" when result!.Message is not null:
                return new BaizeBatchResult(
                    requestId,
                    BaizeBatchItemState.Succeeded,
                    Response: ToLlmResponse(result.Message));

            case "succeeded":
                return new BaizeBatchResult(
                    requestId,
                    BaizeBatchItemState.Failed,
                    Error: new BaizeError(
                        "Anthropic batch item succeeded without a message body.",
                        LlmClientFailureKind.Protocol));

            case "errored":
                var error = result!.Error;

                return new BaizeBatchResult(
                    requestId,
                    BaizeBatchItemState.Failed,
                    Error: new BaizeError(
                        error?.Message ?? "Anthropic batch item errored.",
                        error is null
                            ? LlmClientFailureKind.Protocol
                            : ClaudeErrorClassifier.ClassifyFailureKind(error.Type ?? string.Empty),
                        ProviderStatus: error?.Type));

            case "canceled":
                return new BaizeBatchResult(
                    requestId,
                    BaizeBatchItemState.Cancelled);

            case "expired":
                return new BaizeBatchResult(
                    requestId,
                    BaizeBatchItemState.Expired);

            default:
                return new BaizeBatchResult(
                    requestId,
                    BaizeBatchItemState.Failed,
                    Error: new BaizeError(
                        $"Anthropic batch item returned unknown result type '{type}'.",
                        LlmClientFailureKind.Protocol));
        }
    }

    private static LlmResponse ToLlmResponse(
        ClaudeMessageResponse message)
    {
        var text = new List<string>();
        var reasoning = new List<string>();
        var toolCalls = new List<LlmToolCall>();
        LlmProviderContinuation? reasoningContinuation = null;

        foreach (var block in message.Content ?? [])
        {
            switch (block.Type)
            {
                case "text":
                    text.Add(block.Text ?? string.Empty);
                    break;

                case "thinking":
                    reasoning.Add(block.Thinking ?? string.Empty);

                    if (!string.IsNullOrEmpty(block.Signature))
                    {
                        reasoningContinuation =
                            new LlmProviderContinuation(
                                Provider: "Claude",
                                Values: new Dictionary<string, string>
                                {
                                    ["signature"] = block.Signature
                                });
                    }

                    break;

                case "redacted_thinking":
                    reasoning.Add(string.Empty);

                    if (!string.IsNullOrEmpty(block.Data))
                    {
                        reasoningContinuation =
                            new LlmProviderContinuation(
                                Provider: "Claude",
                                Values: new Dictionary<string, string>
                                {
                                    ["redactedThinkingData"] = block.Data
                                });
                    }

                    break;

                case "tool_use" when block.Name == ClaudeMessageRequestMapper.StructuredOutputToolName:
                    if (block.Input is { } structuredInput)
                        text.Add(structuredInput.GetRawText());
                    break;

                case "tool_use" when !string.IsNullOrEmpty(block.Name) && block.Input is { } toolInput:
                    toolCalls.Add(new LlmToolCall(
                        block.Id ?? string.Empty,
                        block.Name!,
                        toolInput.GetRawText()));
                    break;
            }
        }

        var usage = message.Usage is null
            ? null
            : new LlmUsage(
                message.Usage.InputTokens,
                message.Usage.OutputTokens,
                (message.Usage.InputTokens ?? 0) + (message.Usage.OutputTokens ?? 0),
                message.Usage.CacheReadInputTokens,
                message.Usage.CacheCreationInputTokens);

        return new LlmResponse(
            Content: string.Concat(text),
            Reasoning: reasoning.Count > 0 ? string.Concat(reasoning) : null,
            FinishReason: message.StopReason,
            Usage: usage,
            ToolCalls: toolCalls.Count > 0 ? toolCalls : null,
            Diagnostics: new LlmProviderDiagnostics(
                Provider: "Claude",
                Api: "batch"),
            ReasoningContinuation: reasoningContinuation);
    }

    private static BaizeBatchState MapState(
        string? status,
        ClaudeMessageBatchRequestCounts? counts)
    {
        switch (status?.ToLowerInvariant())
        {
            case "in_progress":
                return BaizeBatchState.Pending;

            case "processing":
                return BaizeBatchState.Running;

            case "canceled":
                return BaizeBatchState.Cancelled;

            case "expired":
                return BaizeBatchState.Expired;

            case "ended":
                var total = counts?.Total ?? 0;
                var succeeded = counts?.Succeeded ?? 0;

                if (total > 0 && succeeded == total)
                    return BaizeBatchState.Completed;

                if (succeeded > 0)
                    return BaizeBatchState.PartiallyCompleted;

                return total > 0
                    ? BaizeBatchState.Failed
                    : BaizeBatchState.Completed;

            default:
                return BaizeBatchState.Running;
        }
    }
}
