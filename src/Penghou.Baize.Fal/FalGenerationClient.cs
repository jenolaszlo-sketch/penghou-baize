using System.Text.Json;
using System.Text.Json.Nodes;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Fal;

/// <summary>
/// <see cref="IGenerationClient"/> for fal.ai queue-based generation
/// (<c>POST {base}/{model}</c>). fal posts a model-specific JSON input and
/// returns a request id immediately; callers poll
/// <c>GET {base}/requests/{id}/status</c> (via <see cref="GetAsync"/>) until the
/// request reaches <c>COMPLETED</c>, then fetch the model output with
/// <c>GET {base}/requests/{id}</c>. In-flight requests are canceled with
/// <c>PUT {base}/requests/{id}/cancel</c>.
/// <para>
/// fal deliberately contrasts with Runway: the input is arbitrary per-model
/// JSON rather than a fixed schema, the queue reports a <c>position</c> instead
/// of a progress fraction, output assets are storage-backed URLs extracted from
/// an arbitrary result document, and cancellation is a <c>PUT</c> rather than a
/// <c>DELETE</c>. The common Baize surface maps to a conventional fal payload;
/// the native <see cref="SubmitQueueAsync(System.Text.Json.Nodes.JsonNode, CancellationToken)"/>
/// posts any model-faithful payload unchanged.
/// </para>
/// </summary>
public sealed class FalGenerationClient : GenerationClientBase
{
    private readonly Uri _queueUri;
    private readonly string _requestsUriPrefix;

    /// <summary>
    /// Creates a fal queue generation client.
    /// </summary>
    /// <param name="model">The queue model identifier (for example <c>fal-ai/flux/dev</c>).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">The fal.ai API secret.</param>
    /// <param name="baseUrl">The fal.ai queue API base URL (for example <c>https://queue.fal.run</c>).</param>
    /// <param name="capabilities">The declared capabilities of the configured model.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    public FalGenerationClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string baseUrl,
        GenerationCapabilities capabilities,
        string endpointId)
        : base("Fal", endpointId, model, httpClientFactory, apiKey, capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(capabilities);

        var baseAddress = baseUrl.TrimEnd('/');
        _queueUri = new Uri($"{baseAddress}/{model}");
        _requestsUriPrefix = $"{baseAddress}/requests/";
    }

    /// <inheritdoc />
    public override async Task<GenerationOperation> SubmitAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var payload = request switch
        {
            ImageGenerationRequest image => BuildImagePayload(image),
            VideoGenerationRequest video => BuildVideoPayload(video),
            AudioGenerationRequest audio => BuildAudioPayload(audio),
            _ => throw BaizeException.UnsupportedCapability(
                $"Fal endpoint '{EndpointId}' does not support generation request " +
                $"type '{request.GetType().Name}'.")
        };

        var queued = await SubmitQueueAsync(payload, cancellationToken);
        var requestId = queued.RequestId ?? throw new BaizeException(
            "Fal submission returned no request id.",
            GenerationErrorKind.GenerationFailed);

        return new GenerationOperation(
            CreateHandle(requestId),
            GenerationOperationState.Queued,
            ProviderMetadata: new Dictionary<string, object?>
            {
                ["status"] = queued.Status,
                ["provider_id"] = requestId
            });
    }

    /// <inheritdoc />
    public override async Task<GenerationOperation> GetAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default)
    {
        EnsureHandleOwnership(handle);
        if (!Capabilities.Supports(GenerationFeature.OperationRetrieval))
            throw BaizeException.UnsupportedCapability(
                $"Fal endpoint '{EndpointId}' does not support operation retrieval.");

        var snapshot = await GetStatusAsync(handle.Id, cancellationToken);
        return await MapStatusAsync(handle, snapshot, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<GenerationOperation> CancelAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default)
    {
        EnsureHandleOwnership(handle);
        if (!Capabilities.Supports(GenerationFeature.Cancellation))
            throw BaizeException.UnsupportedCapability(
                $"Fal endpoint '{EndpointId}' does not support operation cancellation.");

        await CancelQueueAsync(handle.Id, cancellationToken);
        return new GenerationOperation(handle, GenerationOperationState.Canceled);
    }

    /// <summary>
    /// Submits an arbitrary model-faithful payload to the queue and returns the
    /// queue document. This is the provider-native surface: fal models accept
    /// per-model JSON, so the payload is posted unchanged.
    /// </summary>
    /// <param name="payload">The model-faithful JSON payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The queue-submission response.</returns>
    public async Task<FalQueueResponse> SubmitQueueAsync(
        JsonNode payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _queueUri);
        ApplyAuth(httpRequest);
        httpRequest.Content = JsonContent(payload);

        var response = await SendAsync(httpRequest, "queue submission", submission: true, cancellationToken);
        var root = await ReadJsonAsync(response, "queue submission", cancellationToken);
        return Deserialize<FalQueueResponse>(root, "queue submission");
    }

    /// <summary>
    /// Retrieves a status snapshot for a queued request.
    /// </summary>
    /// <param name="requestId">The provider-assigned request id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current status snapshot.</returns>
    public async Task<FalRequestStatus> GetStatusAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(_requestsUriPrefix + Uri.EscapeDataString(requestId) + "/status"));
        ApplyAuth(httpRequest);

        var response = await SendAsync(httpRequest, "queue status", submission: false, cancellationToken);
        var root = await ReadJsonAsync(response, "queue status", cancellationToken);
        return Deserialize<FalRequestStatus>(root, "queue status");
    }

    /// <summary>
    /// Retrieves the final model output for a completed request. The output
    /// document is arbitrary per-model JSON whose storage-backed asset URLs are
    /// extracted by <see cref="GetAsync"/>.
    /// </summary>
    /// <param name="requestId">The provider-assigned request id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The raw output document.</returns>
    public async Task<JsonElement> GetResultAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(_requestsUriPrefix + Uri.EscapeDataString(requestId)));
        ApplyAuth(httpRequest);

        var response = await SendAsync(httpRequest, "queue result", submission: false, cancellationToken);
        return await ReadJsonAsync(response, "queue result", cancellationToken);
    }

    /// <summary>
    /// Cancels a queued request. fal cancels with <c>PUT .../requests/{id}/cancel</c>
    /// rather than a <c>DELETE</c>.
    /// </summary>
    /// <param name="requestId">The provider-assigned request id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cancellation response.</returns>
    public async Task<FalQueueResponse> CancelQueueAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri(_requestsUriPrefix + Uri.EscapeDataString(requestId) + "/cancel"));
        ApplyAuth(httpRequest);

        var response = await SendAsync(httpRequest, "queue cancellation", submission: false, cancellationToken);
        var root = await ReadJsonAsync(response, "queue cancellation", cancellationToken);
        return Deserialize<FalQueueResponse>(root, "queue cancellation");
    }

    /// <inheritdoc />
    protected override void ApplyAuth(HttpRequestMessage httpRequest)
    {
        if (!string.IsNullOrEmpty(ApiKey))
            httpRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Key", ApiKey);
    }

    private static JsonObject BuildImagePayload(ImageGenerationRequest image)
    {
        var payload = new JsonObject
        {
            ["prompt"] = image.Prompt
        };
        if (image.Inputs.Count > 0)
            payload["image_url"] = FormatMedia(image.Inputs[0], "image/png");
        foreach (var reference in image.Inputs.Skip(1))
            AddReference(payload, reference, "image/png");
        if (image.Count > 1)
            payload["num_images"] = image.Count;
        if (image.Seed is { } seed)
            payload["seed"] = seed;
        if (!string.IsNullOrEmpty(image.AspectRatio))
            payload["aspect_ratio"] = image.AspectRatio;
        if (image.Size is { } imageSize)
            payload["image_size"] = new JsonObject
            {
                ["width"] = imageSize.Width,
                ["height"] = imageSize.Height
            };
        if (!string.IsNullOrEmpty(image.OutputFormat))
            payload["output_format"] = NormalizeFormat(image.OutputFormat);
        return payload;
    }

    private static JsonObject BuildVideoPayload(VideoGenerationRequest video)
    {
        var payload = new JsonObject
        {
            ["prompt"] = video.Prompt
        };
        if (video.SourceVideo is not null)
            payload["input_video"] = FormatMedia(video.SourceVideo, "video/mp4");
        else if (video.FirstFrame is not null)
            payload["image_url"] = FormatMedia(video.FirstFrame, "image/png");
        if (video.LastFrame is not null)
            payload["last_image_url"] = FormatMedia(video.LastFrame, "image/png");
        foreach (var reference in video.References)
            AddReference(payload, reference, "image/png");
        if (video.Duration is { } duration)
            payload["duration"] = (int)Math.Round(duration.TotalSeconds);
        if (video.Seed is { } seed)
            payload["seed"] = seed;
        if (!string.IsNullOrEmpty(video.AspectRatio))
            payload["aspect_ratio"] = video.AspectRatio;
        if (video.Size is { } videoSize)
            payload["video_size"] = new JsonObject
            {
                ["width"] = videoSize.Width,
                ["height"] = videoSize.Height
            };
        if (video.GenerateAudio is { } generateAudio)
            payload["generate_audio"] = generateAudio;
        return payload;
    }

    private static JsonObject BuildAudioPayload(AudioGenerationRequest audio)
    {
        var payload = new JsonObject
        {
            ["prompt"] = audio.Prompt
        };
        if (audio.SourceAudio is not null)
            payload["input_audio"] = FormatMedia(audio.SourceAudio, "audio/mp3");
        if (audio.Voice is { } voice)
            payload["voice"] = voice;
        if (!string.IsNullOrEmpty(audio.OutputFormat))
            payload["output_format"] = NormalizeFormat(audio.OutputFormat);
        if (audio.Duration is { } duration)
            payload["duration"] = (int)Math.Round(duration.TotalSeconds);
        return payload;
    }

    private static void AddReference(
        JsonObject payload,
        LlmMediaSource reference,
        string defaultMimeType)
    {
        if (payload["reference_image_urls"] is not JsonArray references)
        {
            references = [];
            payload["reference_image_urls"] = references;
        }

        references.Add(FormatMedia(reference, defaultMimeType));
    }

    /// <summary>Accepts bare (<c>png</c>) or MIME-style (<c>image/png</c>) formats and sends the bare form.</summary>
    private static string NormalizeFormat(string outputFormat) =>
        outputFormat.Contains('/') &&
        outputFormat.Split('/') is [_, var subtype]
            ? subtype
            : outputFormat;

    private static string FormatMedia(LlmMediaSource source, string defaultMimeType) =>
        source switch
        {
            LlmUriSource uri => uri.Uri.ToString(),
            LlmInlineDataSource inline =>
                $"data:{defaultMimeType};base64," + Convert.ToBase64String(inline.Data.ToArray()),
            LlmProviderFileSource => throw BaizeException.UnsupportedCapability(
                "Fal endpoints do not accept provider-file media inputs."),
            _ => throw BaizeException.InvalidRequest(
                $"Unsupported media input source '{source.GetType().Name}'.")
        };

    private async Task<GenerationOperation> MapStatusAsync(
        GenerationOperationHandle handle,
        FalRequestStatus snapshot,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["status"] = snapshot.Status,
            ["provider_id"] = snapshot.RequestId ?? handle.Id
        };
        if (snapshot.Position is { } position)
            metadata["queue_position"] = position;
        if (snapshot.Metrics is { } metrics)
        {
            if (metrics.QueueTime is { } queueTime)
                metadata["queue_time"] = queueTime;
            if (metrics.InferenceTime is { } inferenceTime)
                metadata["inference_time"] = inferenceTime;
            if (metrics.TotalTime is { } totalTime)
                metadata["total_time"] = totalTime;
        }

        var state = MapState(snapshot.Status);
        switch (state)
        {
            case GenerationOperationState.Queued:
            case GenerationOperationState.Running:
            case GenerationOperationState.Canceled:
            case GenerationOperationState.Unknown:
                return new GenerationOperation(handle, state, ProviderMetadata: metadata);

            case GenerationOperationState.Failed:
                return new GenerationOperation(
                    handle,
                    GenerationOperationState.Failed,
                    Error: new GenerationError(
                        GenerationErrorKind.GenerationFailed,
                        "fal request failed.",
                        ProviderStatus: snapshot.Status),
                    ProviderMetadata: metadata);

            default:
                var output = await GetResultAsync(handle.Id, cancellationToken);
                if (ExtractError(output) is { Length: > 0 } detail)
                {
                    return new GenerationOperation(
                        handle,
                        GenerationOperationState.Failed,
                        Error: new GenerationError(
                            GenerationErrorKind.GenerationFailed,
                            detail,
                            ProviderStatus: snapshot.Status),
                        ProviderMetadata: metadata);
                }

                var assets = ExtractAssets(output);
                if (assets.Count == 0)
                    throw new BaizeException(
                        "fal request completed without a usable output URL.",
                        GenerationErrorKind.GenerationFailed);

                return new GenerationOperation(
                    handle,
                    GenerationOperationState.Succeeded,
                    new GenerationResult(
                        assets,
                        Metadata: new Dictionary<string, object?>
                        {
                            ["raw_output"] = output.ToString()
                        }),
                    ProviderMetadata: metadata);
        }
    }

    private static GenerationOperationState MapState(string? status) =>
        status switch
        {
            "IN_QUEUE" => GenerationOperationState.Queued,
            "IN_PROGRESS" => GenerationOperationState.Running,
            "COMPLETED" => GenerationOperationState.Succeeded,
            "ERROR" => GenerationOperationState.Failed,
            "CANCELED" or "CANCELLED" => GenerationOperationState.Canceled,
            _ => GenerationOperationState.Unknown
        };

    private static string? ExtractError(JsonElement output)
    {
        if (output.ValueKind != JsonValueKind.Object)
            return null;

        if (output.TryGetProperty("status", out var errorStatus) &&
            string.Equals(errorStatus.GetString(), "ERROR", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetString(output, "detail", out var detail))
                return detail;
            if (TryGetString(output, "message", out var message))
                return message;
            return "fal request completed with an ERROR status.";
        }

        if (output.TryGetProperty("detail", out var detailDoc) &&
            detailDoc.ValueKind == JsonValueKind.String)
        {
            return detailDoc.GetString();
        }

        if (output.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String)
                return error.GetString();
            if (error.ValueKind == JsonValueKind.Object)
            {
                if (TryGetString(error, "detail", out var errorDetail))
                    return errorDetail;
                if (TryGetString(error, "message", out var errorMessage))
                    return errorMessage;
                return "fal request reported an error document.";
            }
        }

        return null;
    }

    private static bool TryGetString(JsonElement element, string property, out string? value)
    {
        if (element.TryGetProperty(property, out var candidate) && candidate.ValueKind == JsonValueKind.String)
        {
            value = candidate.GetString();
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Recursively harvests absolute <c>http(s)</c> asset URLs from an arbitrary
    /// fal output document. fal models return model-specific JSON (for example
    /// <c>{ "images": [{ "url": ... }] }</c> or <c>{ "video": { "url": ... } }</c>),
    /// so a shape-agnostic walk is the only honest way to surface storage-backed
    /// assets generically.
    /// </summary>
    private static List<GeneratedAsset> ExtractAssets(JsonElement output)
    {
        var assets = new List<GeneratedAsset>();
        CollectAssetUrls(output, assets);
        return assets;
    }

    private static void CollectAssetUrls(JsonElement element, List<GeneratedAsset> assets)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (element.GetString() is { } text &&
                    Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    assets.Add(new GeneratedAsset(
                        new UriGeneratedAssetSource(uri),
                        ContentType: InferContentType(uri)));
                }
                break;

            case JsonValueKind.Object:
                // fal storage-backed assets are objects such as
                // { "url", "content_type", "file_name", "file_size" } (the
                // documented ImageFile/VideoFile shape). Preserve the provider's
                // own metadata when the url property is present; otherwise walk
                // the object generically.
                if (TryReadAssetObject(element, out var asset))
                {
                    assets.Add(asset);
                    break;
                }
                foreach (var property in element.EnumerateObject())
                    CollectAssetUrls(property.Value, assets);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectAssetUrls(item, assets);
                break;
        }
    }

    private static bool TryReadAssetObject(JsonElement element, out GeneratedAsset asset)
    {
        asset = null!;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("url", out var urlElement) ||
            urlElement.ValueKind != JsonValueKind.String ||
            urlElement.GetString() is not { } urlText ||
            !Uri.TryCreate(urlText, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        string? contentType = null;
        string? fileName = null;
        long? size = null;
        if (TryGetString(element, "content_type", out var documentedContentType))
            contentType = documentedContentType;
        if (TryGetString(element, "file_name", out var documentedFileName))
            fileName = documentedFileName;
        if (element.TryGetProperty("file_size", out var sizeElement) &&
            sizeElement.ValueKind == JsonValueKind.Number &&
            sizeElement.TryGetInt64(out var parsedSize))
        {
            size = parsedSize;
        }

        asset = new GeneratedAsset(
            new UriGeneratedAssetSource(uri),
            ContentType: contentType ?? InferContentType(uri),
            FileName: fileName,
            Size: size);
        return true;
    }

    private static string? InferContentType(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".flac" => "audio/flac",
            _ => null
        };
    }

    private void EnsureHandleOwnership(GenerationOperationHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!string.Equals(handle.Provider, "Fal", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(handle.EndpointId, EndpointId, StringComparison.Ordinal))
        {
            throw BaizeException.InvalidRequest(
                $"Handle '{handle.Provider}/{handle.EndpointId}/{handle.Id}' does not belong to " +
                $"Fal endpoint '{EndpointId}'.");
        }
    }
}