using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Gemini;

/// <summary>
/// <see cref="IGenerationClient"/> for Gemini image generation through the
/// Interactions API (<c>POST /v1beta/interactions</c> with an image-capable
/// model such as <c>gemini-3.1-flash-lite-image</c>). Generation is synchronous;
/// a completed interaction yields one or more inline image assets. Text-to-image
/// and image-to-image (editing with input images) are supported. Operation
/// retrieval and cancellation are not applicable to the synchronous interaction
/// path and are rejected before a provider call.
/// </summary>
public sealed class GeminiGenerationClient : GenerationClientBase
{
    private readonly Uri _interactionsUri;
    private readonly string _model;
    private readonly string? _defaultImageSize;
    private readonly string _defaultInputImageMimeType;
    private readonly bool _storeResponses;

    /// <summary>
    /// Creates a Gemini generation client.
    /// </summary>
    /// <param name="model">The image-capable Gemini model identifier (for example <c>gemini-3.1-flash-lite-image</c>).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">The Gemini API key.</param>
    /// <param name="baseUrl">Base API URL. When it does not already include a version segment such as <c>v1beta</c>, <c>v1beta</c> is appended.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="imageSize">The default requested output image size (for example <c>1K</c>, <c>2K</c>, <c>4K</c>).</param>
    /// <param name="defaultInputImageMimeType">The MIME type assumed for inline image inputs that carry no content type.</param>
    /// <param name="storeResponses">Whether responses are stored on the provider for later retrieval.</param>
    public GeminiGenerationClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string baseUrl,
        GenerationCapabilities capabilities,
        string endpointId,
        string? imageSize = null,
        string defaultInputImageMimeType = "image/png",
        bool storeResponses = false)
        : base("Gemini", endpointId, model, httpClientFactory, apiKey, capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultInputImageMimeType);

        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var lastSegment = normalizedBaseUrl[(normalizedBaseUrl.LastIndexOf('/') + 1)..];
        var includeVersionSegment = !LooksLikeApiVersion(lastSegment);
        _interactionsUri = new Uri(
            $"{normalizedBaseUrl}" +
            $"{(includeVersionSegment ? "/v1beta" : string.Empty)}" +
            "/interactions");
        _model = model;
        _defaultImageSize = imageSize;
        _defaultInputImageMimeType = defaultInputImageMimeType;
        _storeResponses = storeResponses;
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
            _ => throw BaizeException.UnsupportedCapability(
                $"Gemini endpoint '{EndpointId}' does not support generation request " +
                $"type '{request.GetType().Name}'.")
        };
    }

    /// <inheritdoc />
    public override Task<GenerationOperation> GetAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default)
    {
        EnsureHandleOwnership(handle);
        throw BaizeException.UnsupportedCapability(
            $"Gemini endpoint '{EndpointId}' does not support operation retrieval.");
    }

    /// <inheritdoc />
    public override Task<GenerationOperation> CancelAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default)
    {
        EnsureHandleOwnership(handle);
        throw BaizeException.UnsupportedCapability(
            $"Gemini endpoint '{EndpointId}' does not support operation cancellation.");
    }

    private async Task<GenerationOperation> SubmitImageAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var wire = new GeminiInteractionsRequest
        {
            Model = _model,
            Store = _storeResponses ? null : false,
            Input = BuildInput(request),
            ResponseFormat = BuildResponseFormat(request)
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _interactionsUri);
        ApplyAuth(httpRequest);
        httpRequest.Content = JsonContent(wire);
        var response = await SendAsync(httpRequest, "image submission", submission: true, cancellationToken);
        var root = await ReadJsonAsync(response, "image submission", cancellationToken);
        var payload = Deserialize<GeminiInteractionsResponse>(root, "image submission");

        var images = ExtractImageParts(payload);
        if (images.Count == 0)
        {
            if (!string.Equals(payload.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return new GenerationOperation(
                    CreateHandle(payload.Id ?? Guid.NewGuid().ToString("N")),
                    GenerationOperationState.Unknown,
                    ProviderMetadata: BuildMetadata(payload));
            }

            throw new BaizeException(
                "Gemini image submission returned no image.",
                GenerationErrorKind.GenerationFailed,
                providerStatus: payload.Error?.Message);
        }

        var assets = images
            .Select(image => MapImageAsset(image, request.OutputFormat))
            .ToList();

        return new GenerationOperation(
            CreateHandle(payload.Id ?? Guid.NewGuid().ToString("N")),
            GenerationOperationState.Succeeded,
            new GenerationResult(assets),
            ProviderMetadata: BuildMetadata(payload));
    }

    private List<GeminiInteractionPart> ExtractImageParts(GeminiInteractionsResponse payload)
    {
        var parts = new List<GeminiInteractionPart>();
        foreach (var step in payload.Steps ?? [])
        {
            foreach (var content in step.Content ?? [])
            {
                if (IsImagePart(content))
                    parts.Add(content);
            }
        }

        if (parts.Count == 0 && payload.OutputImage is { } outputImage && IsImagePart(outputImage))
            parts.Add(outputImage);

        return parts;
    }

    private static bool IsImagePart(GeminiInteractionPart part) =>
        string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(part.Data);

    private static GeneratedAsset MapImageAsset(GeminiInteractionPart image, string? outputFormat)
    {
        var contentType = image.MimeType ?? NormalizeMimeType(outputFormat, "image/png");
        var bytes = Convert.FromBase64String(image.Data!);
        return new GeneratedAsset(
            new InlineGeneratedAssetSource(bytes, contentType),
            ContentType: contentType,
            Size: bytes.Length);
    }

    private List<GeminiInteractionPart> BuildInput(ImageGenerationRequest request)
    {
        var parts = new List<GeminiInteractionPart>
        {
            new() { Type = "text", Text = request.Prompt }
        };

        foreach (var input in request.Inputs)
        {
            switch (input)
            {
                case LlmInlineDataSource inline:
                    parts.Add(new GeminiInteractionPart
                    {
                        Type = "image",
                        MimeType = _defaultInputImageMimeType,
                        Data = Convert.ToBase64String(inline.Data.ToArray())
                    });
                    break;
                case LlmUriSource uri:
                    parts.Add(new GeminiInteractionPart
                    {
                        Type = "image",
                        Uri = uri.Uri.ToString()
                    });
                    break;
                case LlmProviderFileSource file:
                    throw BaizeException.UnsupportedCapability(
                        $"Gemini endpoint '{EndpointId}' does not accept provider-file image inputs.");
            }
        }

        return parts;
    }

    private GeminiImageResponseFormat? BuildResponseFormat(ImageGenerationRequest request)
    {
        var format = new GeminiImageResponseFormat
        {
            MimeType = NormalizeMimeType(request.OutputFormat, "image/png")
        };
        var aspectRatio = request.AspectRatio;
        if (aspectRatio is null && request.Size is { } size)
            aspectRatio = $"{size.Width}:{size.Height}";
        if (aspectRatio is not null)
            format.AspectRatio = aspectRatio;
        if (_defaultImageSize is not null)
            format.ImageSize = _defaultImageSize;
        return format;
    }

    private static string NormalizeMimeType(string? outputFormat, string fallback)
    {
        if (string.IsNullOrWhiteSpace(outputFormat))
            return fallback;
        return outputFormat.Contains('/') ? outputFormat : $"image/{outputFormat}";
    }

    private static IReadOnlyDictionary<string, object?>? BuildMetadata(GeminiInteractionsResponse payload)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["status"] = payload.Status,
            ["provider_id"] = payload.Id
        };
        if (payload.Model is not null)
            metadata["model"] = payload.Model;
        return metadata;
    }

    /// <summary>Gemini authenticates with the <c>x-goog-api-key</c> header.</summary>
    protected override void ApplyAuth(HttpRequestMessage httpRequest)
    {
        if (!string.IsNullOrEmpty(ApiKey))
            httpRequest.Headers.Add("x-goog-api-key", ApiKey);
    }

    private void EnsureHandleOwnership(GenerationOperationHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!string.Equals(handle.Provider, "Gemini", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(handle.EndpointId, EndpointId, StringComparison.Ordinal))
        {
            throw BaizeException.InvalidRequest(
                $"Handle '{handle.Provider}/{handle.EndpointId}/{handle.Id}' does not belong to " +
                $"Gemini endpoint '{EndpointId}'.");
        }
    }

    private static bool LooksLikeApiVersion(string segment) =>
        segment.Length >= 2 &&
        segment[0] == 'v' &&
        segment.Skip(1).TakeWhile(char.IsDigit).Any();
}
