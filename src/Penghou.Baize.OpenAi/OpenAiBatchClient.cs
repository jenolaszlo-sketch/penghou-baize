using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// <see cref="IBaizeBatchClient"/> implementation for the OpenAI Batch API
/// (<c>POST /batches</c>). Submits requests as a JSONL file upload followed by
/// a batch creation, then polls the batch status and retrieves the output
/// file. The client is stateless: a submitted batch can be resumed purely
/// through its serializable <see cref="ProviderBatchHandle"/>.
/// </summary>
public sealed class OpenAiBatchClient : BaizeBatchClientBase
{
    private const string BatchEndpoint = "/v1/chat/completions";
    private const string CompletionWindow = "24h";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy =
                JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    private readonly Uri _filesUri;
    private readonly Uri _batchesUri;
    private readonly OpenAiDialect _dialect;
    private readonly LlmEndpointCapabilities _effectiveCapabilities;

    /// <summary>Wire display name; the registry provider key is "OpenAi".</summary>
    protected override string ProviderDisplayName => "OpenAI";

    /// <summary>Applies the OpenAI bearer scheme.</summary>
    protected override void ApplyAuth(HttpRequestMessage request) =>
        ApplyBearerAuth(request);

    /// <summary>
    /// Creates an OpenAI Batch API client.
    /// </summary>
    /// <param name="model">The model identifier (for example <c>gpt-4o-mini</c>).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">The OpenAI API key.</param>
    /// <param name="baseUrl">Base API URL, for example <c>https://api.openai.com/v1</c>; must end in <c>/v1</c> or the batch paths are appended directly.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="dialect">The OpenAI-compatible wire dialect of the endpoint.</param>
    public OpenAiBatchClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string baseUrl,
        LlmEndpointCapabilities capabilities,
        OpenAiDialect dialect = OpenAiDialect.Standard)
        : base("OpenAi", model, httpClientFactory, apiKey, OpenAiDialectPolicy.Apply(capabilities, dialect))
    {
        _dialect = dialect;
        _effectiveCapabilities = OpenAiDialectPolicy.Apply(capabilities, dialect);
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        _filesUri = new Uri($"{normalizedBaseUrl}/files");
        _batchesUri = new Uri($"{normalizedBaseUrl}/batches");
    }

    /// <inheritdoc />
    public override async Task<ProviderBatchHandle> SubmitAsync(
        IReadOnlyList<BaizeBatchItem> items,
        BatchSubmissionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        BatchRequestValidator.ValidateItems(items, ProviderId);

        foreach (var item in items)
            LlmRequestValidator.Validate(Model, _effectiveCapabilities, item.Request);

        var jsonl = BuildJsonl(items);

        var fileId = await UploadInputFileAsync(
            jsonl,
            cancellationToken);

        using var createRequest =
            new HttpRequestMessage(HttpMethod.Post, _batchesUri);

        ApplyAuth(createRequest);

        if (!string.IsNullOrEmpty(options?.IdempotencyKey))
        {
            createRequest.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                options.IdempotencyKey);
        }

        var createBody = new
        {
            input_file_id = fileId,
            endpoint = BatchEndpoint,
            completion_window = CompletionWindow,
            metadata = options?.Metadata
        };

        createRequest.Content = new StringContent(
            JsonSerializer.Serialize(createBody, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var batch = await SendAsync<OpenAiBatch>(
            createRequest,
            JsonOptions,
            cancellationToken);

        if (string.IsNullOrEmpty(batch.Id))
            throw new LlmClientException(
                "OpenAI batch creation returned no batch identifier.",
                LlmClientFailureKind.Protocol);

        var metadata = new Dictionary<string, string>
        {
            ["input_file_id"] = fileId
        };

        return new ProviderBatchHandle(
            ProviderId: ProviderId,
            BatchId: batch.Id!,
            Metadata: metadata);
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

        var batch = await SendAsync<OpenAiBatch>(
            request,
            JsonOptions,
            cancellationToken);

        if (string.IsNullOrEmpty(batch.Id))
            throw new LlmClientException(
                "OpenAI batch creation returned no batch identifier.",
                LlmClientFailureKind.Protocol);

        return new ProviderBatchStatus(
            State: MapState(batch.Status),
            ProviderStatus: batch.Status,
            Total: batch.RequestCounts?.Total,
            Completed: batch.RequestCounts?.Completed,
            Failed: batch.RequestCounts?.Failed);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        var batch = await GetBatchAsync(handle, cancellationToken);

        if (string.IsNullOrEmpty(batch.OutputFileId) &&
            string.IsNullOrEmpty(batch.ErrorFileId))
        {
            throw new LlmClientException(
                $"OpenAI batch '{handle.BatchId}' has no output or error file yet " +
                $"(state '{batch.Status}'); results cannot be retrieved.",
                LlmClientFailureKind.Protocol);
        }

        var results = new List<BaizeBatchResult>();

        if (!string.IsNullOrEmpty(batch.OutputFileId))
            results.AddRange(await ReadResultsFileAsync(batch.OutputFileId, cancellationToken));

        if (!string.IsNullOrEmpty(batch.ErrorFileId))
            results.AddRange(await ReadResultsFileAsync(batch.ErrorFileId, cancellationToken));

        var duplicate = results
            .GroupBy(result => result.RequestId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new LlmClientException(
                $"OpenAI batch returned duplicate result id '{duplicate.Key}'.",
                LlmClientFailureKind.Protocol);
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
                "This OpenAI endpoint does not support batch cancellation.");
        }

        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{_batchesUri}/{handle.BatchId}/cancel"));

        ApplyAuth(request);

        await SendAsync<OpenAiBatch>(request, JsonOptions, cancellationToken);
    }

    private async Task<OpenAiBatch> GetBatchAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{_batchesUri}/{handle.BatchId}"));

        ApplyAuth(request);

        return await SendAsync<OpenAiBatch>(request, JsonOptions, cancellationToken);
    }

    private async Task<IReadOnlyList<BaizeBatchResult>> ReadResultsFileAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{_filesUri}/{fileId}/content"));
        ApplyAuth(request);

        var httpClient = CreateTransport();
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new LlmClientException(
                $"OpenAI batch results retrieval failed with HTTP {(int)response.StatusCode}: {content}",
                (int)response.StatusCode);
        }

        var results = new List<BaizeBatchResult>();

        foreach (var rawLine in SplitJsonl(content))
        {
            OpenAiBatchOutputLine? line;

            try
            {
                line = JsonSerializer.Deserialize<OpenAiBatchOutputLine>(rawLine, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new LlmClientException(
                    $"Failed to parse OpenAI batch result line: {rawLine}",
                    ex);
            }

            if (line is null || string.IsNullOrWhiteSpace(line.CustomId))
            {
                throw new LlmClientException(
                    $"OpenAI batch result line has no custom_id: {rawLine}",
                    LlmClientFailureKind.Protocol);
            }

            results.Add(NormalizeResult(line));
        }

        return results;
    }

    private async Task<string> UploadInputFileAsync(
        string jsonl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _filesUri);

        ApplyAuth(request);

        using var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(
            Encoding.UTF8.GetBytes(jsonl));

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("text/jsonl");

        form.Add(fileContent, "file", "batch-input.jsonl");
        form.Add(new StringContent("batch"), "purpose");

        request.Content = form;

        var file = await SendAsync<OpenAiFile>(
            request,
            JsonOptions,
            cancellationToken);

        if (string.IsNullOrEmpty(file.Id))
            throw new LlmClientException(
                "OpenAI file upload returned no file identifier.",
                LlmClientFailureKind.Protocol);

        return file.Id!;
    }

    private string BuildJsonl(IReadOnlyList<BaizeBatchItem> items)
    {
        var builder = new StringBuilder();

        foreach (var item in items)
        {
            OpenAiChatCompletionRequestMapper.Validate(
                Model,
                _effectiveCapabilities,
                _dialect,
                item.Request);

            var wireRequest = OpenAiChatCompletionRequestMapper.Build(
                Model,
                _effectiveCapabilities,
                _dialect,
                item.Request,
                streaming: false);

            var line = new
            {
                custom_id = item.RequestId,
                method = "POST",
                url = BatchEndpoint,
                body = wireRequest
            };

            builder.AppendLine(
                JsonSerializer.Serialize(line, JsonOptions));
        }

        return builder.ToString();
    }


    private static BaizeBatchResult NormalizeResult(
        OpenAiBatchOutputLine line)
    {
        var requestId = line.CustomId!;

        var statusCode = line.Response?.StatusCode;

        if (statusCode is not (>= 200 and < 300) || line.Error is not null)
        {
            var (message, providerStatus) = ReadError(
                line.Response?.Body,
                line.Error);

            return new BaizeBatchResult(
                requestId,
                BaizeBatchItemState.Failed,
                Error: new BaizeError(
                    message,
                    statusCode is { } code
                        ? LlmClientException.ClassifyStatusCode(code)
                        : LlmClientFailureKind.Protocol,
                    statusCode,
                    providerStatus));
        }

        var body = line.Response!.Body;

        if (body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty("error", out _))
        {
            var (message, providerStatus) = ReadError(body, null);

            return new BaizeBatchResult(
                requestId,
                BaizeBatchItemState.Failed,
                Error: new BaizeError(
                    message,
                    LlmClientException.ClassifyStatusCode(statusCode.Value),
                    statusCode,
                    providerStatus));
        }

        OpenAiChatCompletionResponse completion;

        try
        {
            completion = body.Deserialize<OpenAiChatCompletionResponse>(
                    JsonOptions)
                ?? throw new LlmClientException(
                    "OpenAI batch completion body was empty.",
                    LlmClientFailureKind.Protocol);
        }
        catch (JsonException ex)
        {
            throw new LlmClientException(
                $"Failed to parse OpenAI batch completion body: {body.GetRawText()}",
                ex);
        }

        if (completion.Choices is not { Count: > 0 } ||
            completion.Choices[0].Message is null)
        {
            throw new LlmClientException(
                "OpenAI batch completion body contained no choices.",
                LlmClientFailureKind.Protocol);
        }

        return new BaizeBatchResult(
            requestId,
            BaizeBatchItemState.Succeeded,
            Response: ToLlmResponse(completion));
    }

    private static (string Message, string? ProviderStatus) ReadError(
        JsonElement? body,
        OpenAiBatchOutputError? error)
    {
        if (error is not null)
        {
            return (
                error.Message ?? "OpenAI batch item failed.",
                string.IsNullOrEmpty(error.Type)
                    ? error.Code
                    : error.Type);
        }

        if (body is { ValueKind: JsonValueKind.Object } element &&
            element.TryGetProperty("error", out var errorElement) &&
            errorElement.ValueKind == JsonValueKind.Object)
        {
            var message = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;
            var type = errorElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            var code = errorElement.TryGetProperty("code", out var codeElement) &&
                       codeElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? codeElement.ToString()
                : null;

            return (
                message ?? "OpenAI batch item failed.",
                string.IsNullOrEmpty(type) ? code : type);
        }

        if (body is { ValueKind: JsonValueKind.String } textElement)
        {
            return (
                textElement.GetString() ?? "OpenAI batch item failed.",
                null);
        }

        return ("OpenAI batch item failed.", null);
    }

    private static LlmResponse ToLlmResponse(
        OpenAiChatCompletionResponse completion)
    {
        var choice = completion.Choices![0];
        var message = choice.Message!;

        var syntheticOutput = message.ToolCalls?
            .FirstOrDefault(call => string.Equals(
                call.Function.Name,
                OpenAiStructuredOutput.ToolName,
                StringComparison.Ordinal));
        var toolCalls = message.ToolCalls is { Count: > 0 }
            ? message.ToolCalls
                .Where(call => !string.Equals(
                    call.Function.Name,
                    OpenAiStructuredOutput.ToolName,
                    StringComparison.Ordinal))
                .Select(call => new LlmToolCall(
                    call.Id,
                    call.Function.Name,
                    call.Function.Arguments))
                .ToList()
            : null;

        var usage = completion.Usage is null
            ? null
            : new LlmUsage(
                completion.Usage.PromptTokens,
                completion.Usage.CompletionTokens,
                completion.Usage.TotalTokens,
                completion.Usage.PromptCacheHitTokens,
                completion.Usage.PromptCacheMissTokens);

        return new LlmResponse(
            Content: syntheticOutput?.Function.Arguments ??
                ReadMessageText(message.Content),
            Reasoning: message.ReasoningContent,
            FinishReason: choice.FinishReason,
            Usage: usage,
            ToolCalls: toolCalls,
            Diagnostics: new LlmProviderDiagnostics(
                Provider: "OpenAi",
                ActualModel: completion.Model,
                Api: "batch",
                ResponseId: completion.Id,
                ServiceTier: completion.ServiceTier,
                SystemFingerprint: completion.SystemFingerprint));
    }

    private static string ReadMessageText(object? content) => content switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element =>
            element.GetString() ?? string.Empty,
        _ => string.Empty
    };

    private static BaizeBatchState MapState(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "validating" or "queued" => BaizeBatchState.Pending,
            "in_progress" or "finalizing" => BaizeBatchState.Running,
            "completed" => BaizeBatchState.Completed,
            "failed" => BaizeBatchState.Failed,
            "expired" => BaizeBatchState.Expired,
            "cancelling" => BaizeBatchState.Cancelling,
            "cancelled" => BaizeBatchState.Cancelled,
            _ => BaizeBatchState.Running
        };
}
