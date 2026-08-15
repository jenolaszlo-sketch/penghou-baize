using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Penghou.Baize.Generation;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// <see cref="IGenerationClient"/> for OpenAI's explicit artifact-generation
/// APIs: image generation/editing (<c>/images/generations</c>, <c>/images/edits</c>),
/// video generation (<c>/videos</c>) and speech (<c>/audio/speech</c>). Explicit
/// artifact requests are never routed through chat. Immediate image and speech
/// results return <see cref="GenerationOperationState.Succeeded"/> directly;
/// video returns a queued operation that is polled with
/// <see cref="IGenerationClient.GetAsync"/>.
/// </summary>
public sealed class OpenAiGenerationClient : GenerationClientBase
{
    private readonly Uri _imageGenerationsUri;
    private readonly Uri _imageEditsUri;
    private readonly Uri _videosUri;
    private readonly Uri _audioSpeechUri;
    private readonly string _imageModel;
    private readonly string _videoModel;
    private readonly string _audioModel;
    private readonly string _defaultVoice;

    /// <summary>
    /// Creates an OpenAI generation client.
    /// </summary>
    /// <param name="model">The model the endpoint is bound to (the operation handle model).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">The API key, or an empty string for anonymous endpoints.</param>
    /// <param name="baseAddress">The API base address, for example <c>https://api.openai.com/v1</c>.</param>
    /// <param name="capabilities">The advertised generation capabilities.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="imageModel">Image-generation model override; falls back to <paramref name="model"/>.</param>
    /// <param name="videoModel">Video-generation model override; falls back to <paramref name="model"/>.</param>
    /// <param name="audioModel">Speech-generation model override; falls back to <paramref name="model"/>.</param>
    /// <param name="defaultVoice">The default speech voice.</param>
    public OpenAiGenerationClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        Uri baseAddress,
        GenerationCapabilities capabilities,
        string endpointId,
        string? imageModel = null,
        string? videoModel = null,
        string? audioModel = null,
        string? defaultVoice = null)
        : base("OpenAi", endpointId, model, httpClientFactory, apiKey, capabilities)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        var baseUrl = baseAddress.ToString().TrimEnd('/');
        _imageGenerationsUri = new Uri($"{baseUrl}/images/generations");
        _imageEditsUri = new Uri($"{baseUrl}/images/edits");
        _videosUri = new Uri($"{baseUrl}/videos");
        _audioSpeechUri = new Uri($"{baseUrl}/audio/speech");
        _imageModel = imageModel ?? model;
        _videoModel = videoModel ?? model;
        _audioModel = audioModel ?? model;
        _defaultVoice = defaultVoice ?? "alloy";
    }

    /// <inheritdoc />
    public override async Task<GenerationOperation> SubmitAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request switch
        {
            ImageGenerationRequest image => await SubmitImageAsync(image, cancellationToken),
            VideoGenerationRequest video => await SubmitVideoAsync(video, cancellationToken),
            AudioGenerationRequest audio => await SubmitAudioAsync(audio, cancellationToken),
            _ => throw BaizeException.UnsupportedCapability(
                $"OpenAI endpoint '{EndpointId}' does not support generation request " +
                $"type '{request.GetType().Name}'.")
        };
    }

    /// <inheritdoc />
    public override async Task<GenerationOperation> GetAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default)
    {
        EnsureHandleOwnership(handle);
        if (!Capabilities.Supports(GenerationFeature.OperationRetrieval))
            throw BaizeException.UnsupportedCapability(
                $"OpenAI endpoint '{EndpointId}' does not support operation retrieval.");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, new Uri($"{_videosUri}/{handle.Id}"));
        ApplyAuth(httpRequest);
        var response = await SendAsync(httpRequest, "video status", submission: false, cancellationToken);
        var root = await ReadJsonAsync(response, "video status", cancellationToken);
        var video = Deserialize<OpenAiVideo>(root, "video status");
        return MapVideoOperation(handle, video, root);
    }

    /// <inheritdoc />
    public override async Task<GenerationOperation> CancelAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default)
    {
        EnsureHandleOwnership(handle);
        if (!Capabilities.Supports(GenerationFeature.Cancellation))
            throw BaizeException.UnsupportedCapability(
                $"OpenAI endpoint '{EndpointId}' does not support operation cancellation.");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Delete, new Uri($"{_videosUri}/{handle.Id}"));
        ApplyAuth(httpRequest);
        var response = await SendAsync(httpRequest, "video cancellation", submission: false, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new GenerationOperation(
            handle,
            MapVideoStateFromBody(body, handle),
            ProviderMetadata: new Dictionary<string, object?>
            {
                ["provider_status"] = body.Trim().Length == 0 ? null : body.Trim()
            });
    }

    private async Task<GenerationOperation> SubmitImageAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var isEdit = request.Inputs.Count > 0;
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            isEdit ? _imageEditsUri : _imageGenerationsUri);
        ApplyAuth(httpRequest);

        if (isEdit)
        {
            httpRequest.Content = BuildEditContent(request);
        }
        else
        {
            var wire = new OpenAiImageGenerationRequest
            {
                Model = _imageModel,
                Prompt = request.Prompt,
                N = request.Count,
                Size = FormatSize(request.Size),
                AspectRatio = request.AspectRatio,
                OutputFormat = request.OutputFormat,
                Seed = request.Seed
            };
            httpRequest.Content = JsonContent(wire);
        }

        var response = await SendAsync(httpRequest, "image submission", submission: true, cancellationToken);
        var root = await ReadJsonAsync(response, "image submission", cancellationToken);
        var payload = Deserialize<OpenAiImageGenerationResponse>(root, "image submission");

        var assets = (payload.Data ?? [])
            .Select((data, index) => MapImageAsset(data, request.OutputFormat, index))
            .ToList();

        if (assets.Count == 0)
            throw new BaizeException(
                "OpenAI image submission returned no images.",
                GenerationErrorKind.GenerationFailed);

        var metadata = new Dictionary<string, object?>();
        if (payload.Created is { } created)
            metadata["created"] = created;
        var revised = payload.Data?.FirstOrDefault(data => !string.IsNullOrEmpty(data.RevisedPrompt))?.RevisedPrompt;
        if (revised is not null)
            metadata["revised_prompt"] = revised;

        return new GenerationOperation(
            CreateHandle(Guid.NewGuid().ToString("N")),
            GenerationOperationState.Succeeded,
            new GenerationResult(assets, metadata));
    }

    private async Task<GenerationOperation> SubmitVideoAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var wire = new OpenAiVideoGenerationRequest
        {
            Model = _videoModel,
            Prompt = request.Prompt,
            Size = FormatVideoSize(request.Size),
            Seed = request.Seed
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _videosUri);
        ApplyAuth(httpRequest);
        httpRequest.Content = JsonContent(wire);

        var response = await SendAsync(httpRequest, "video submission", submission: true, cancellationToken);
        var root = await ReadJsonAsync(response, "video submission", cancellationToken);
        var video = Deserialize<OpenAiVideo>(root, "video submission");
        var handle = CreateHandle(video.Id ?? Guid.NewGuid().ToString("N"));
        return MapVideoOperation(handle, video, root);
    }

    private async Task<GenerationOperation> SubmitAudioAsync(
        AudioGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var responseFormat = NormalizeSpeechFormat(request.OutputFormat);
        var wire = new OpenAiSpeechGenerationRequest
        {
            Model = _audioModel,
            Input = request.Prompt,
            Voice = request.Voice ?? _defaultVoice,
            ResponseFormat = responseFormat
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _audioSpeechUri);
        ApplyAuth(httpRequest);
        httpRequest.Content = JsonContent(wire);

        var response = await SendAsync(httpRequest, "speech submission", submission: true, cancellationToken);
        var bytes = await ReadBytesAsync(response, cancellationToken);

        if (bytes.Length == 0)
            throw new BaizeException(
                "OpenAI speech submission returned no audio.",
                GenerationErrorKind.GenerationFailed);

        var asset = new GeneratedAsset(
            new InlineGeneratedAssetSource(bytes, ContentTypeForSpeechFormat(responseFormat)),
            ContentType: ContentTypeForSpeechFormat(responseFormat),
            Size: bytes.Length);

        return new GenerationOperation(
            CreateHandle(Guid.NewGuid().ToString("N")),
            GenerationOperationState.Succeeded,
            new GenerationResult([asset]));
    }

    private static GeneratedAsset MapImageAsset(
        OpenAiImageData data,
        string? outputFormat,
        int index)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["index"] = index
        };
        if (data.RevisedPrompt is not null)
            metadata["revised_prompt"] = data.RevisedPrompt;

        if (!string.IsNullOrEmpty(data.B64Json))
        {
            var contentType = ContentTypeForImageFormat(outputFormat);
            var bytes = Convert.FromBase64String(data.B64Json);
            return new GeneratedAsset(
                new InlineGeneratedAssetSource(bytes, contentType),
                ContentType: contentType,
                Size: bytes.Length,
                Metadata: metadata);
        }

        if (data.Url is not null && Uri.TryCreate(data.Url, UriKind.Absolute, out var url))
        {
            return new GeneratedAsset(
                new UriGeneratedAssetSource(url),
                ContentType: ContentTypeForImageFormat(outputFormat),
                Metadata: metadata);
        }

        throw new BaizeException(
            "OpenAI image submission returned an image with no URL or inline data.",
            GenerationErrorKind.GenerationFailed);
    }

    private GenerationOperation MapVideoOperation(
        GenerationOperationHandle handle,
        OpenAiVideo video,
        JsonElement root)
    {
        var state = MapVideoState(video.Status, handle);

        IReadOnlyDictionary<string, object?>? providerMetadata = null;
        if (root.ValueKind != JsonValueKind.Undefined)
        {
            providerMetadata = new Dictionary<string, object?>
            {
                ["status"] = video.Status,
                ["provider_id"] = video.Id,
            };
        }

        if (state != GenerationOperationState.Succeeded)
        {
            GenerationError? error = video.Error is { } failure
                ? new GenerationError(
                    GenerationErrorKind.GenerationFailed,
                    failure.Message ?? "OpenAI video generation failed.",
                    ProviderStatus: failure.Code)
                : state == GenerationOperationState.Failed
                    ? new GenerationError(
                        GenerationErrorKind.GenerationFailed,
                        "OpenAI video generation failed.")
                    : null;

            return new GenerationOperation(
                handle,
                state,
                Error: error,
                Progress: ClampProgress(video.Progress),
                ProviderMetadata: providerMetadata);
        }

        var assets = MapVideoAssets(video);
        if (assets.Count == 0)
            throw new BaizeException(
                "OpenAI video completed without a usable output.",
                GenerationErrorKind.GenerationFailed);

        return new GenerationOperation(
            handle,
            state,
            new GenerationResult(assets),
            Progress: ClampProgress(video.Progress),
            ProviderMetadata: providerMetadata);
    }

    private static List<GeneratedAsset> MapVideoAssets(OpenAiVideo video)
    {
        var assets = new List<GeneratedAsset>();
        foreach (var content in video.Content ?? [])
        {
            if (content.Url is not null && Uri.TryCreate(content.Url, UriKind.Absolute, out var url))
                assets.Add(new GeneratedAsset(
                    new UriGeneratedAssetSource(url),
                    ContentType: content.Type));
        }

        if (video.Output is not null &&
            Uri.TryCreate(video.Output, UriKind.Absolute, out var outputUrl))
        {
            assets.Add(new GeneratedAsset(
                new UriGeneratedAssetSource(outputUrl),
                ContentType: "video/mp4"));
        }

        return assets;
    }

    private static GenerationOperationState MapVideoState(string? status, GenerationOperationHandle handle) =>
        status switch
        {
            "queued" => GenerationOperationState.Queued,
            "in_progress" => GenerationOperationState.Running,
            "completed" => GenerationOperationState.Succeeded,
            "failed" => GenerationOperationState.Failed,
            _ => GenerationOperationState.Unknown
        };

    private static GenerationOperationState MapVideoStateFromBody(string body, GenerationOperationHandle handle) =>
        string.IsNullOrWhiteSpace(body)
            ? GenerationOperationState.Unknown
            : MapVideoState(ExtractStatus(body), handle);

    private static string? ExtractStatus(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("status", out var status) &&
                   status.ValueKind == JsonValueKind.String
                ? status.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private HttpContent BuildEditContent(ImageGenerationRequest request)
    {
        var form = new MultipartFormDataContent();
        var inlineIndex = 0;
        var referenceIndex = 0;

        foreach (var input in request.Inputs)
        {
            switch (input)
            {
                case LlmInlineDataSource inline:
                    form.Add(
                        new ByteArrayContent(inline.Data.ToArray()),
                        "image",
                        $"input-{inlineIndex++}.bin");
                    break;
                case LlmUriSource uri:
                    form.Add(
                        new StringContent(uri.Uri.ToString(), Encoding.UTF8),
                        "reference_image",
                        $"reference-{referenceIndex++}.txt");
                    break;
                case LlmProviderFileSource file:
                    throw BaizeException.UnsupportedCapability(
                        $"OpenAI endpoint '{EndpointId}' does not accept provider-file image inputs.");
            }
        }

        form.Add(new StringContent(request.Prompt, Encoding.UTF8), "prompt");
        if (!string.IsNullOrEmpty(_imageModel))
            form.Add(new StringContent(_imageModel, Encoding.UTF8), "model");
        if (request.Count != 1)
            form.Add(new StringContent(request.Count.ToString(), Encoding.UTF8), "n");
        if (FormatSize(request.Size) is { } size)
            form.Add(new StringContent(size, Encoding.UTF8), "size");
        if (request.OutputFormat is not null)
            form.Add(new StringContent(request.OutputFormat, Encoding.UTF8), "output_format");
        if (request.Seed is { } seed)
            form.Add(new StringContent(seed.ToString(), Encoding.UTF8), "seed");

        return form;
    }

    private void EnsureHandleOwnership(GenerationOperationHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!string.Equals(handle.Provider, "OpenAi", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(handle.EndpointId, EndpointId, StringComparison.Ordinal))
        {
            throw BaizeException.InvalidRequest(
                $"Handle '{handle.Provider}/{handle.EndpointId}/{handle.Id}' does not belong to " +
                $"OpenAI endpoint '{EndpointId}'.");
        }
    }

    private static string? FormatSize(GenerationImageSize? size) =>
        size is null ? null : $"{size.Width}x{size.Height}";

    private static string? FormatVideoSize(GenerationVideoSize? size) =>
        size is null ? null : $"{size.Width}x{size.Height}";

    private static double? ClampProgress(double? progress) =>
        progress is null ? null : Math.Clamp(progress.Value, 0.0, 1.0);

    private static string? NormalizeSpeechFormat(string? format) =>
        string.IsNullOrWhiteSpace(format) ? "mp3" : format;

    private static string ContentTypeForSpeechFormat(string? format) =>
        format?.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "opus" => "audio/opus",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "pcm" => "audio/pcm",
            _ when format?.Contains('/') is true => format,
            _ => "audio/mpeg"
        };

    private static string ContentTypeForImageFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return "image/png";
        if (format.Contains('/'))
            return format;
        return format.ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpeg" or "jpg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            "avif" => "image/avif",
            _ => "image/png"
        };
    }
}