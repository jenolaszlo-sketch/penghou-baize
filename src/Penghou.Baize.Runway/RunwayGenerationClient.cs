using System.Text.Json;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Runway;

/// <summary>
/// <see cref="IGenerationClient"/> for Runway video generation through the
/// Runway Developer API (<c>/v1/text_to_video</c> and <c>/v1/image_to_video</c>).
/// Generation is asynchronous: creation returns a task id, and callers poll
/// <c>GET /v1/tasks/{id}</c> (via <see cref="GetAsync"/>) until the task reaches
/// <c>SUCCEEDED</c> or <c>FAILED</c>. Tasks can be canceled with
/// <c>DELETE /v1/tasks/{id}</c>.
/// </summary>
public sealed class RunwayGenerationClient : GenerationClientBase
{
    private readonly Uri _textToVideoUri;
    private readonly Uri _imageToVideoUri;
    private readonly Uri _uploadsUri;
    private readonly string _tasksUriPrefix;
    private readonly string _apiVersion;
    private readonly string _defaultInputImageMimeType;
    private readonly string? _defaultRatio;
    private readonly string? _defaultOutputFormat;

    /// <summary>
    /// Creates a Runway generation client.
    /// </summary>
    /// <param name="model">The video-generation model identifier (for example <c>gen4.5</c>).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">The Runway API secret.</param>
    /// <param name="baseAddress">Base API URL, typically including the <c>v1</c> segment.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="apiVersion">The dated API version sent in the <c>X-Runway-Version</c> header.</param>
    /// <param name="defaultInputImageMimeType">The MIME type assumed for inline image inputs that carry no content type.</param>
    /// <param name="defaultRatio">The default output aspect ratio applied when a request does not specify one.</param>
    /// <param name="defaultOutputFormat">The default output format applied when a request does not specify one.</param>
    public RunwayGenerationClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        Uri baseAddress,
        GenerationCapabilities capabilities,
        string endpointId,
        string? apiVersion = null,
        string? defaultInputImageMimeType = null,
        string? defaultRatio = null,
        string? defaultOutputFormat = null)
        : base("Runway", endpointId, model, httpClientFactory, apiKey, capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var baseUrl = baseAddress.ToString().TrimEnd('/');
        _textToVideoUri = new Uri($"{baseUrl}/text_to_video");
        _imageToVideoUri = new Uri($"{baseUrl}/image_to_video");
        _uploadsUri = new Uri($"{baseUrl}/uploads");
        _tasksUriPrefix = $"{baseUrl}/tasks/";
        _apiVersion = apiVersion ?? "2024-11-06";
        _defaultInputImageMimeType = defaultInputImageMimeType ?? "image/png";
        _defaultRatio = defaultRatio;
        _defaultOutputFormat = defaultOutputFormat;
    }

    /// <inheritdoc />
    public override async Task<GenerationOperation> SubmitAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request switch
        {
            VideoGenerationRequest video => await SubmitVideoAsync(video, cancellationToken),
            _ => throw BaizeException.UnsupportedCapability(
                $"Runway endpoint '{EndpointId}' does not support generation request " +
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
                $"Runway endpoint '{EndpointId}' does not support operation retrieval.");

        var task = await GetTaskAsync(handle.Id, cancellationToken);
        return MapTask(handle, task);
    }

    /// <inheritdoc />
    public override async Task<GenerationOperation> CancelAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default)
    {
        EnsureHandleOwnership(handle);
        if (!Capabilities.Supports(GenerationFeature.Cancellation))
            throw BaizeException.UnsupportedCapability(
                $"Runway endpoint '{EndpointId}' does not support operation cancellation.");

        await CancelTaskAsync(handle.Id, cancellationToken);
        return new GenerationOperation(handle, GenerationOperationState.Canceled);
    }

    /// <summary>
    /// Submits a text-to-video task and returns the provider task id.
    /// </summary>
    /// <param name="request">The provider-faithful request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task-creation response.</returns>
    public async Task<RunwayTaskCreateResponse> CreateTextToVideoAsync(
        RunwayTextToVideoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _textToVideoUri);
        ApplyAuth(httpRequest);
        httpRequest.Content = JsonContent(request);

        var response = await SendAsync(httpRequest, "text-to-video submission", submission: true, cancellationToken);
        var root = await ReadJsonAsync(response, "text-to-video submission", cancellationToken);
        return Deserialize<RunwayTaskCreateResponse>(root, "text-to-video submission");
    }

    /// <summary>
    /// Submits an image-to-video task and returns the provider task id.
    /// </summary>
    /// <param name="request">The provider-faithful request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task-creation response.</returns>
    public async Task<RunwayTaskCreateResponse> CreateImageToVideoAsync(
        RunwayImageToVideoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _imageToVideoUri);
        ApplyAuth(httpRequest);
        httpRequest.Content = JsonContent(request);

        var response = await SendAsync(httpRequest, "image-to-video submission", submission: true, cancellationToken);
        var root = await ReadJsonAsync(response, "image-to-video submission", cancellationToken);
        return Deserialize<RunwayTaskCreateResponse>(root, "image-to-video submission");
    }

    /// <summary>
    /// Retrieves a task snapshot by id.
    /// </summary>
    /// <param name="taskId">The provider-assigned task id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current task snapshot.</returns>
    public async Task<RunwayTask> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(_tasksUriPrefix + Uri.EscapeDataString(taskId)));
        ApplyAuth(httpRequest);

        var response = await SendAsync(httpRequest, "task status", submission: false, cancellationToken);
        var root = await ReadJsonAsync(response, "task status", cancellationToken);
        return Deserialize<RunwayTask>(root, "task status");
    }

    /// <summary>
    /// Cancels a task by id.
    /// </summary>
    /// <param name="taskId">The provider-assigned task id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task CancelTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri(_tasksUriPrefix + Uri.EscapeDataString(taskId)));
        ApplyAuth(httpRequest);

        await SendAsync(httpRequest, "task cancellation", submission: false, cancellationToken);
    }

    /// <summary>
    /// Reserves an ephemeral upload slot for a media file. The returned
    /// <see cref="RunwayUploadCreateResponse"/> carries a presigned
    /// <see cref="RunwayUploadCreateResponse.UploadUrl"/> plus form
    /// <see cref="RunwayUploadCreateResponse.Fields"/>; complete the upload with
    /// <see cref="UploadFileAsync"/>, then use the resulting
    /// <see cref="RunwayUploadCreateResponse.RunwayUri"/> as an input image or
    /// video reference in generation requests.
    /// </summary>
    /// <param name="filename">The file name with a valid media extension (for example <c>first-frame.png</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reserved upload slot.</returns>
    public async Task<RunwayUploadCreateResponse> CreateEphemeralUploadAsync(
        string filename,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _uploadsUri);
        ApplyAuth(httpRequest);
        httpRequest.Content = JsonContent(new RunwayUploadCreateRequest { Filename = filename });

        var response = await SendAsync(httpRequest, "ephemeral upload", submission: false, cancellationToken);
        var root = await ReadJsonAsync(response, "ephemeral upload", cancellationToken);
        return Deserialize<RunwayUploadCreateResponse>(root, "ephemeral upload");
    }

    /// <summary>
    /// Completes a reserved ephemeral upload by posting the file bytes to the
    /// presigned upload URL. Returns the <c>runway://</c> URI that generation
    /// requests can use as an input image or video reference.
    /// </summary>
    /// <param name="upload">The reserved upload slot.</param>
    /// <param name="data">The file bytes to upload.</param>
    /// <param name="filename">The file name used for the file part.</param>
    /// <param name="contentType">The media content type (for example <c>image/png</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>runway://</c> URI referencing the uploaded file.</returns>
    public async Task<string> UploadFileAsync(
        RunwayUploadCreateResponse upload,
        ReadOnlyMemory<byte> data,
        string filename,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (data.IsEmpty)
            throw new ArgumentException("Upload data cannot be empty.", nameof(data));
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (string.IsNullOrWhiteSpace(upload.UploadUrl))
            throw new BaizeException(
                "Runway upload reservation returned no presigned upload URL.",
                GenerationErrorKind.GenerationFailed);
        if (upload.Fields is null)
            throw new BaizeException(
                "Runway upload reservation returned no multipart fields.",
                GenerationErrorKind.GenerationFailed);

        var form = new MultipartFormDataContent();
        foreach (var (name, value) in upload.Fields)
            form.Add(new StringContent(value, System.Text.Encoding.UTF8), name);
        form.Add(new ByteArrayContent(data.ToArray())
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType) }
        }, "file", filename);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(upload.UploadUrl, UriKind.Absolute))
        {
            Content = form
        };

        var response = await SendAsync(httpRequest, "ephemeral upload", submission: false, cancellationToken);

        if (string.IsNullOrWhiteSpace(upload.RunwayUri))
            throw new BaizeException(
                "Runway upload reservation returned no runway:// URI.",
                GenerationErrorKind.GenerationFailed);
        return upload.RunwayUri;
    }

    /// <inheritdoc />
    protected override void ApplyAuth(HttpRequestMessage httpRequest)
    {
        base.ApplyAuth(httpRequest);
        httpRequest.Headers.Add("X-Runway-Version", _apiVersion);
    }

    private async Task<GenerationOperation> SubmitVideoAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        RunwayTaskCreateResponse payload;
        if (request.FirstFrame is not null)
        {
            payload = await CreateImageToVideoAsync(
                new RunwayImageToVideoRequest
                {
                    Model = Model,
                    PromptImage = FormatPromptImage(request.FirstFrame),
                    PromptText = request.Prompt,
                    Ratio = request.AspectRatio ?? _defaultRatio,
                    Duration = FormatDuration(request.Duration),
                    Seed = request.Seed,
                    OutputFormat = _defaultOutputFormat,
                    Audio = request.GenerateAudio
                },
                cancellationToken);
        }
        else
        {
            payload = await CreateTextToVideoAsync(
                new RunwayTextToVideoRequest
                {
                    Model = Model,
                    PromptText = request.Prompt,
                    Ratio = request.AspectRatio ?? _defaultRatio,
                    Duration = FormatDuration(request.Duration),
                    Seed = request.Seed,
                    OutputFormat = _defaultOutputFormat,
                    Audio = request.GenerateAudio
                },
                cancellationToken);
        }

        var taskId = payload.Id ?? throw new BaizeException(
            "Runway submission returned no task id.",
            GenerationErrorKind.GenerationFailed);

        var metadata = new Dictionary<string, object?>();
        if (payload.EstimatedCost?.Credits is { } estimated)
            metadata["estimated_cost_credits"] = estimated;

        return new GenerationOperation(
            CreateHandle(taskId),
            GenerationOperationState.Queued,
            ProviderMetadata: metadata);
    }

    private GenerationOperation MapTask(
        GenerationOperationHandle handle,
        RunwayTask task)
    {
        var state = MapState(task.Status);

        var metadata = new Dictionary<string, object?>
        {
            ["status"] = task.Status,
            ["provider_id"] = task.Id,
            ["created_at"] = task.CreatedAt
        };
        if (task.EstimatedCost?.Credits is { } estimated)
            metadata["estimated_cost_credits"] = estimated;
        if (task.Cost?.Credits is { } cost)
            metadata["cost_credits"] = cost;

        if (state != GenerationOperationState.Succeeded)
        {
            GenerationError? error = state == GenerationOperationState.Failed
                ? new GenerationError(
                    GenerationErrorKind.GenerationFailed,
                    task.Failure ?? "Runway video generation failed.",
                    ProviderStatus: task.FailureCode)
                : null;

            return new GenerationOperation(
                handle,
                state,
                Error: error,
                Progress: ClampProgress(task.Progress),
                ProviderMetadata: metadata);
        }

        var assets = MapOutputAssets(task.Output);
        if (assets.Count == 0)
            throw new BaizeException(
                "Runway video completed without a usable output.",
                GenerationErrorKind.GenerationFailed);

        return new GenerationOperation(
            handle,
            state,
            new GenerationResult(assets),
            Progress: ClampProgress(task.Progress),
            ProviderMetadata: metadata);
    }

    private string FormatPromptImage(LlmMediaSource source) =>
        source switch
        {
            LlmUriSource uri => uri.Uri.ToString(),
            LlmInlineDataSource inline =>
                $"data:{_defaultInputImageMimeType};base64," +
                Convert.ToBase64String(inline.Data.ToArray()),
            LlmProviderFileSource file when file.Provider == new LlmProviderKey("Runway") =>
                file.FileId,
            LlmProviderFileSource file => throw BaizeException.UnsupportedCapability(
                $"Runway endpoint '{EndpointId}' does not accept files owned by " +
                $"provider '{file.Provider}'."),
            _ => throw BaizeException.InvalidRequest(
                $"Unsupported image input source '{source.GetType().Name}'.")
        };

    private static List<GeneratedAsset> MapOutputAssets(IReadOnlyList<string>? output)
    {
        var assets = new List<GeneratedAsset>();
        foreach (var url in output ?? [])
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                assets.Add(new GeneratedAsset(
                    new UriGeneratedAssetSource(absolute),
                    ContentType: "video/mp4"));
            }
        }

        return assets;
    }

    private static GenerationOperationState MapState(string? status) =>
        status switch
        {
            "PENDING" or "THROTTLED" => GenerationOperationState.Queued,
            "RUNNING" => GenerationOperationState.Running,
            "SUCCEEDED" => GenerationOperationState.Succeeded,
            "FAILED" => GenerationOperationState.Failed,
            "CANCELLED" => GenerationOperationState.Canceled,
            _ => GenerationOperationState.Unknown
        };

    private static int? FormatDuration(TimeSpan? duration) =>
        duration is null ? null : Math.Max(1, (int)Math.Round(duration.Value.TotalSeconds));

    private static double? ClampProgress(double? progress) =>
        progress is null ? null : Math.Clamp(progress.Value, 0.0, 1.0);

    private void EnsureHandleOwnership(GenerationOperationHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!string.Equals(handle.Provider, "Runway", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(handle.EndpointId, EndpointId, StringComparison.Ordinal))
        {
            throw BaizeException.InvalidRequest(
                $"Handle '{handle.Provider}/{handle.EndpointId}/{handle.Id}' does not belong to " +
                $"Runway endpoint '{EndpointId}'.");
        }
    }
}