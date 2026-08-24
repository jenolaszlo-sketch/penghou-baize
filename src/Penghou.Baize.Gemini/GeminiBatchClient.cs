using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize;

namespace Penghou.Baize.Gemini;

/// <summary>
/// <see cref="IBaizeBatchClient"/> implementation for the Gemini Batch API
/// (<c>POST /v1beta/models/{model}:batchGenerateContent</c>). Submits requests
/// as a JSONL file upload followed by a heavyweight job creation, then polls
/// the long-running operation and retrieves the results file. The client is
/// stateless: a submitted batch can be resumed purely through its serializable
/// <see cref="ProviderBatchHandle"/>.
/// </summary>
public sealed class GeminiBatchClient : IBaizeBatchClient
{
    private const string InputFileName = "batch-input.jsonl";
    private const string InputFileMimeType = "application/jsonl";
    private const string DefaultDisplayName = "penghou-batch";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _model;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly Uri _uploadUri;
    private readonly Uri _createUri;
    private readonly string _rootBase;
    private readonly string _versionedBase;
    private readonly string _apiVersion;
    private readonly LlmEndpointCapabilities _capabilities;
    private readonly ILlmSchemaAdapter _schemaAdapter;

    /// <inheritdoc />
    public string ProviderId => "Gemini";

    /// <inheritdoc />
    public BatchCapabilities Capabilities => _capabilities.Batch;

    /// <summary>
    /// Creates a Gemini Batch API client.
    /// </summary>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="model">The Gemini model identifier (for example <c>gemini-2.5-flash</c>).</param>
    /// <param name="apiKey">The Gemini API key.</param>
    /// <param name="baseUrl">Base API URL. When it does not already include a version segment such as <c>v1beta</c> or <c>v1</c>, <c>v1beta</c> is appended.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="schemaAdapter">Adapter for Gemini's native JSON Schema dialect.</param>
    public GeminiBatchClient(
        IHttpClientFactory httpClientFactory,
        string model,
        string apiKey,
        string baseUrl,
        LlmEndpointCapabilities capabilities,
        ILlmSchemaAdapter? schemaAdapter = null)
    {
        _httpClientFactory = httpClientFactory;
        _model = model;
        _apiKey = apiKey;
        _capabilities = capabilities;
        _schemaAdapter = schemaAdapter ?? GeminiSchemaAdapter.Default;

        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var lastSegment =
            normalizedBaseUrl[
                (normalizedBaseUrl.LastIndexOf('/') + 1)..];
        var includeVersionSegment = !GeminiUrl.LooksLikeApiVersion(lastSegment);
        var rootBase = includeVersionSegment
            ? normalizedBaseUrl
            : normalizedBaseUrl[..normalizedBaseUrl.LastIndexOf('/')];
        _rootBase = rootBase;

        // Gemini's asynchronous batch and file APIs currently use v1beta,
        // even when a caller supplies a versioned chat base URL.
        _apiVersion = "v1beta";
        _versionedBase = $"{rootBase}/{_apiVersion}";
        _uploadUri = new Uri($"{rootBase}/upload/{_apiVersion}/files");
        _createUri = new Uri($"{_versionedBase}/models/{model}:batchGenerateContent");
    }

    /// <inheritdoc />
    public async Task<ProviderBatchHandle> SubmitAsync(
        IReadOnlyList<BaizeBatchItem> items,
        BatchSubmissionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        BatchRequestValidator.ValidateItems(items, ProviderId);

        foreach (var item in items)
            GeminiMessageRequestMapper.Validate(_model, _capabilities, item.Request);

        var jsonl = BuildJsonl(items);

        var inputFile = await UploadInputFileAsync(
            jsonl,
            cancellationToken);

        var createBody = new
        {
            batch = new
            {
                display_name = ReadDisplayName(options),
                input_config = new
                {
                    file_name = inputFile
                }
            }
        };

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _createUri);

        SetApiKey(createRequest);

        createRequest.Content = new StringContent(
            JsonSerializer.Serialize(createBody, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var operation = await SendAsync<GeminiBatchOperation>(
            createRequest,
            cancellationToken);

        if (string.IsNullOrEmpty(operation.Name))
            throw new LlmClientException(
                "Gemini batch creation returned no batch identifier.",
                LlmClientFailureKind.Protocol);

        var metadata = new Dictionary<string, string>
        {
            ["input_file_id"] = inputFile
        };

        return new ProviderBatchHandle(
            ProviderId: ProviderId,
            BatchId: operation.Name!,
            Metadata: metadata);
    }

    /// <inheritdoc />
    public async Task<ProviderBatchStatus> GetStatusAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        var operation = await GetOperationAsync(
            handle,
            cancellationToken);

        var batch = ReadCompletedBatch(operation.Result);
        var stats = operation.Metadata?.BatchStats ?? batch?.Stats;
        var state = operation.Metadata?.State ?? batch?.State;

        return new ProviderBatchStatus(
            State: MapState(
                state,
                operation.Done),
            ProviderStatus: state,
            Total: stats?.RequestCount,
            Completed: stats?.SuccessfulRequestCount,
            Failed: stats?.FailedRequestCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        var operation = await GetOperationAsync(
            handle,
            cancellationToken);

        if (operation.Result is not { ValueKind: JsonValueKind.Object } result)
        {
            throw new LlmClientException(
                $"Gemini batch '{handle.BatchId}' has no results yet " +
                $"(state '{operation.Metadata?.State}'); results cannot be retrieved.",
                LlmClientFailureKind.Protocol);
        }

        // The long-running operation has used both response shapes in the
        // wild: older/sample payloads wrap the destination in `output`, while
        // current paid-tier v1beta responses expose `responsesFile` directly.
        var fileName = ReadNestedString(result, "responsesFile") ??
            ReadNestedString(result, "output", "responsesFile");

        if (fileName is not null)
        {
            var content = await DownloadResultsFileAsync(
                fileName,
                cancellationToken);

            return ParseResults(content);
        }

        // Inline results are returned directly on the operation response.
        var inlined = ReadArray(
                result,
                "inlinedResponses",
                "inlinedResponses") ??
            ReadArray(
                result,
                "output",
                "inlinedResponses",
                "inlinedResponses");

        if (inlined is not null)
        {
            var results = new List<BaizeBatchResult>();

            foreach (var element in inlined)
            {
                var line = element.ValueKind != JsonValueKind.Object
                    ? null
                    : element.Deserialize<GeminiBatchResultLine>(JsonOptions);

                if (line is null || string.IsNullOrWhiteSpace(line.Key))
                {
                    throw new LlmClientException(
                        $"Gemini inline batch result has no correlation key: {element.GetRawText()}",
                        LlmClientFailureKind.Protocol);
                }

                results.Add(NormalizeResult(line));
            }

            return results;
        }

        throw new LlmClientException(
            $"Gemini batch '{handle.BatchId}' reported no result destination.",
            LlmClientFailureKind.Protocol);
    }

    /// <inheritdoc />
    public async Task CancelAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default)
    {
        if (!Capabilities.HasFlag(BatchCapabilities.Cancellation))
        {
            throw new NotSupportedException(
                "This Gemini endpoint does not support batch cancellation.");
        }

        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{_versionedBase}/{handle.BatchId}:cancel"));

        SetApiKey(request);

        await SendAsync<JsonElement>(
            request,
            cancellationToken);
    }

    private async Task<GeminiBatchOperation> GetOperationAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken)
    {
        BatchRequestValidator.ValidateHandle(handle, ProviderId);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{_versionedBase}/{handle.BatchId}"));

        SetApiKey(request);

        return await SendAsync<GeminiBatchOperation>(
            request,
            cancellationToken);
    }

    private async Task<string> UploadInputFileAsync(
        string jsonl,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(jsonl);
        using var startRequest = new HttpRequestMessage(HttpMethod.Post, _uploadUri);
        SetApiKey(startRequest);
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "start");
        startRequest.Headers.TryAddWithoutValidation(
            "X-Goog-Upload-Header-Content-Length",
            bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startRequest.Headers.TryAddWithoutValidation(
            "X-Goog-Upload-Header-Content-Type",
            InputFileMimeType);
        startRequest.Content = new StringContent(
            JsonSerializer.Serialize(
                new { file = new { display_name = InputFileName } },
                JsonOptions),
            Encoding.UTF8,
            "application/json");

        var httpClient = _httpClientFactory.CreateClient(BaizeHttp.ClientName);
        using var startResponse = await httpClient.SendAsync(
            startRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var startBody = await startResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!startResponse.IsSuccessStatusCode)
        {
            throw new LlmClientException(
                $"Gemini file upload initialization failed with HTTP {(int)startResponse.StatusCode}: {startBody}",
                (int)startResponse.StatusCode);
        }

        if (!startResponse.Headers.TryGetValues("X-Goog-Upload-URL", out var uploadUrls) ||
            string.IsNullOrWhiteSpace(uploadUrls.FirstOrDefault()))
        {
            throw new LlmClientException(
                "Gemini file upload initialization returned no X-Goog-Upload-URL header.",
                LlmClientFailureKind.Protocol);
        }

        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(uploadUrls.First()!));
        SetApiKey(uploadRequest);
        uploadRequest.Headers.TryAddWithoutValidation(
            "X-Goog-Upload-Command",
            "upload, finalize");
        uploadRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Offset", "0");
        uploadRequest.Content = new ByteArrayContent(bytes);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(InputFileMimeType);

        var upload = await SendAsync<GeminiFileUploadResponse>(
            uploadRequest,
            cancellationToken);
        var file = upload.File;

        if (string.IsNullOrEmpty(file?.Name))
            throw new LlmClientException(
                "Gemini file upload returned no file identifier.",
                LlmClientFailureKind.Protocol);

        return file.Name!;
    }

    private async Task<string> DownloadResultsFileAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{_rootBase}/download/{_apiVersion}/{fileName}:download?alt=media"));

        SetApiKey(request);

        var httpClient = _httpClientFactory.CreateClient(BaizeHttp.ClientName);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new LlmClientException(
                $"Gemini batch results file retrieval failed with HTTP {(int)response.StatusCode}: {content}",
                (int)response.StatusCode);
        }

        return content;
    }

    private string BuildJsonl(IReadOnlyList<BaizeBatchItem> items)
    {
        var builder = new StringBuilder();

        foreach (var item in items)
        {
            var wireRequest = GeminiMessageRequestMapper.Build(
                _model,
                _capabilities,
                item.Request,
                _schemaAdapter,
                _apiVersion);

            var line = new
            {
                key = item.RequestId,
                request = wireRequest
            };

            builder.AppendLine(
                JsonSerializer.Serialize(line, JsonOptions));
        }

        return builder.ToString();
    }

    private IReadOnlyList<BaizeBatchResult> ParseResults(string content)
    {
        var results = new List<BaizeBatchResult>();

        foreach (var rawLine in SplitJsonl(content))
        {
            GeminiBatchResultLine? line;

            try
            {
                line = JsonSerializer.Deserialize<GeminiBatchResultLine>(
                    rawLine,
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new LlmClientException(
                    $"Failed to parse Gemini batch result line: {rawLine}",
                    ex);
            }

            if (line is null || string.IsNullOrWhiteSpace(line.Key))
            {
                throw new LlmClientException(
                    $"Gemini batch result line has no correlation key: {rawLine}",
                    LlmClientFailureKind.Protocol);
            }

            results.Add(NormalizeResult(line));
        }

        return results;
    }

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(BaizeHttp.ClientName);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new LlmClientException(
                $"Gemini batch request failed with HTTP {(int)response.StatusCode}: {responseBody}",
                (int)response.StatusCode);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
                ?? throw new LlmClientException(
                    $"Gemini returned an empty {typeof(T).Name} body.",
                    LlmClientFailureKind.Protocol);
        }
        catch (JsonException ex)
        {
            throw new LlmClientException(
                $"Failed to parse Gemini batch response: {responseBody}",
                ex);
        }
    }

    private void SetApiKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Add("x-goog-api-key", _apiKey);
    }

    private static string ReadDisplayName(BatchSubmissionOptions? options) =>
        options?.Metadata is { } metadata &&
        metadata.TryGetValue("display_name", out var name) &&
        !string.IsNullOrWhiteSpace(name)
            ? name
            : DefaultDisplayName;

    private static IEnumerable<string> SplitJsonl(string content)
    {
        using var reader = new StringReader(content);

        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    private static string? ReadNestedString(
        JsonElement root,
        params string[] path)
    {
        var current = root;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : null;
    }

    private static List<JsonElement>? ReadArray(
        JsonElement root,
        params string[] path)
    {
        var current = root;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.Array
            ? current.EnumerateArray().ToList()
            : null;
    }

    private static BaizeBatchResult NormalizeResult(
        GeminiBatchResultLine line)
    {
        var requestId = line.Key!;

        if (line.Response is { ValueKind: JsonValueKind.Object } response)
        {
            GeminiChatResponse completion;

            try
            {
                completion = response.Deserialize<GeminiChatResponse>(JsonOptions)
                    ?? throw new LlmClientException(
                        "Gemini batch response body was empty.",
                        LlmClientFailureKind.Protocol);
            }
            catch (JsonException ex)
            {
                throw new LlmClientException(
                    $"Failed to parse Gemini batch response body: {response.GetRawText()}",
                    ex);
            }

            if (completion.Candidates is not { Count: > 0 } ||
                completion.Candidates[0].Content is null)
            {
                return new BaizeBatchResult(
                    requestId,
                    BaizeBatchItemState.Failed,
                    Error: new BaizeError(
                        "Gemini batch item returned no candidate content.",
                        LlmClientFailureKind.Protocol));
            }

            return new BaizeBatchResult(
                requestId,
                BaizeBatchItemState.Succeeded,
                Response: ToLlmResponse(completion));
        }

        if (line.Error is { ValueKind: JsonValueKind.Object } error)
        {
            var (message, code, status) = ReadError(error);

            return new BaizeBatchResult(
                requestId,
                BaizeBatchItemState.Failed,
                Error: new BaizeError(
                    message,
                    GeminiErrorClassifier.ClassifyFailureKind(status, code),
                    code,
                    status));
        }

        return new BaizeBatchResult(
            requestId,
            BaizeBatchItemState.Failed,
            Error: new BaizeError(
                "Gemini batch item returned neither a response nor an error.",
                LlmClientFailureKind.Protocol));
    }

    private static (string Message, int? Code, string? Status) ReadError(
        JsonElement error)
    {
        var message =
            error.TryGetProperty("message", out var messageElement) &&
            messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : null;

        var code = error.TryGetProperty("code", out var codeElement) &&
                   codeElement.ValueKind == JsonValueKind.Number &&
                   codeElement.TryGetInt32(out var codeValue)
            ? codeValue
            : (int?)null;

        var status =
            error.TryGetProperty("status", out var statusElement) &&
            statusElement.ValueKind == JsonValueKind.String
                ? statusElement.GetString()
                : null;

        return (
            message ?? "Gemini batch item failed.",
            code,
            status);
    }

    private static (string? State, GeminiBatchJobStats? Stats)? ReadCompletedBatch(
        JsonElement? result)
    {
        if (result is not { ValueKind: JsonValueKind.Object } value)
            return null;

        var state = value.TryGetProperty("state", out var stateElement) &&
                    stateElement.ValueKind == JsonValueKind.String
            ? stateElement.GetString()
            : null;
        GeminiBatchJobStats? stats = null;

        if (value.TryGetProperty("batchStats", out var statsElement) &&
            statsElement.ValueKind == JsonValueKind.Object)
        {
            stats = statsElement.Deserialize<GeminiBatchJobStats>(JsonOptions);
        }

        return (state, stats);
    }

    private static LlmResponse ToLlmResponse(
        GeminiChatResponse response)
    {
        var candidate = response.Candidates![0];
        var text = new List<string>();
        var reasoning = new List<string>();
        var toolCalls = new List<LlmToolCall>();
        LlmProviderContinuation? reasoningContinuation = null;

        foreach (var part in candidate.Content?.Parts ?? [])
        {
            var continuation = part.ThoughtSignature is null
                ? null
                : new LlmProviderContinuation(
                    Provider: "Gemini",
                    Values: new Dictionary<string, string>
                    {
                        ["thoughtSignature"] =
                            part.ThoughtSignature
                    });

            if (part.Text is not null && part.Thought != true)
            {
                text.Add(part.Text);
            }
            else if (part.Text is not null)
            {
                reasoning.Add(part.Text);

                if (continuation is not null)
                    reasoningContinuation = continuation;
            }

            if (part.FunctionCall is not null)
            {
                toolCalls.Add(new LlmToolCall(
                    part.FunctionCall.Id ??
                        Guid.NewGuid().ToString("N"),
                    part.FunctionCall.Name,
                    part.FunctionCall.Args.GetRawText(),
                    Continuation: continuation));
            }
        }

        var usage = response.Usage is null
            ? null
            : new LlmUsage(
                PromptTokens: response.Usage.PromptTokenCount,
                CompletionTokens: SumGeneratedTokens(
                    response.Usage.CandidatesTokenCount,
                    response.Usage.ThoughtsTokenCount),
                TotalTokens: response.Usage.TotalTokenCount,
                ThinkingTokens: response.Usage.ThoughtsTokenCount);

        return new LlmResponse(
            Content: string.Concat(text),
            Reasoning: reasoning.Count > 0 ? string.Concat(reasoning) : null,
            FinishReason: candidate.FinishReason is null
                ? null
                : MapFinishReason(candidate.FinishReason),
            Usage: usage,
            ToolCalls: toolCalls.Count > 0 ? toolCalls : null,
            Diagnostics: new LlmProviderDiagnostics(
                Provider: "Gemini",
                ActualModel: response.ModelVersion,
                Api: "batch",
                Done: candidate.FinishReason is not null,
                DoneReason: candidate.FinishReason is null
                    ? null
                    : MapFinishReason(candidate.FinishReason),
                NativeToolCallCount: toolCalls.Count,
                ContentLength: text.Sum(item => item.Length),
                ResponseId: response.ResponseId,
                ServiceTier: response.ServiceTier ?? response.Usage?.ServiceTier,
                ThinkingTokens: response.Usage?.ThoughtsTokenCount),
            ReasoningContinuation: reasoningContinuation);
    }

    private static string MapFinishReason(string finishReason) =>
        finishReason switch
        {
            "STOP" => "stop",
            "MAX_OUTPUT_TOKENS" => "length",
            "SAFETY" => "content_filter",
            _ => finishReason.ToLowerInvariant()
        };

    private static int? SumGeneratedTokens(int? candidates, int? thoughts) =>
        candidates.HasValue || thoughts.HasValue
            ? candidates.GetValueOrDefault() + thoughts.GetValueOrDefault()
            : null;

    private static BaizeBatchState MapState(
        string? state,
        bool? done)
    {
        switch (state?.ToUpperInvariant())
        {
            case "BATCH_STATE_PENDING":
            case "JOB_STATE_PENDING":
                return BaizeBatchState.Pending;

            case "BATCH_STATE_RUNNING":
            case "JOB_STATE_PROCESSING":
                return BaizeBatchState.Running;

            case "BATCH_STATE_CANCELLING":
            case "JOB_STATE_CANCELLING":
                return BaizeBatchState.Cancelling;

            case "BATCH_STATE_SUCCEEDED":
            case "JOB_STATE_SUCCEEDED":
                return BaizeBatchState.Completed;

            case "BATCH_STATE_FAILED":
            case "JOB_STATE_FAILED":
                return BaizeBatchState.Failed;

            case "BATCH_STATE_CANCELLED":
            case "JOB_STATE_CANCELLED":
                return BaizeBatchState.Cancelled;

            case "BATCH_STATE_EXPIRED":
            case "JOB_STATE_EXPIRED":
                return BaizeBatchState.Expired;

            default:
                // A terminal operation without an explicit state is treated as
                // completed; anything else is still running.
                return done == true
                    ? BaizeBatchState.Completed
                    : BaizeBatchState.Running;
        }
    }

}
