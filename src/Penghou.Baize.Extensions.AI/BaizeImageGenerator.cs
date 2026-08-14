using Microsoft.Extensions.AI;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Extensions.AI;

// The Microsoft.Extensions.AI image-generation surface is experimental
// (MEAI001). This adapter intentionally opts into it until the ecosystem
// contract stabilizes; Baize-native contracts are unaffected.
#pragma warning disable MEAI001

/// <summary>
/// Adapts a Baize <see cref="IGenerationClient"/> to the experimental standard
/// .NET <see cref="IImageGenerator"/>. The adapter is experimental because
/// <c>IImageGenerator</c> itself is not yet stable; it does not change Baize's
/// provider-neutral generation contracts.
/// </summary>
public sealed class BaizeImageGenerator : IImageGenerator
{
    private readonly IGenerationClient _client;
    private readonly ImageGeneratorMetadata _metadata;
    private readonly string _providerName;

    /// <summary>
    /// Initializes the adapter.
    /// </summary>
    /// <param name="client">The Baize generation client to adapt.</param>
    /// <param name="providerName">The provider name for metadata, when known.</param>
    /// <param name="providerUri">The provider base URI for metadata, when known.</param>
    /// <param name="modelId">The configured model for metadata, when known.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
    public BaizeImageGenerator(
        IGenerationClient client,
        string? providerName = null,
        Uri? providerUri = null,
        string? modelId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _providerName = providerName ?? "Unknown";
        _metadata = new ImageGeneratorMetadata(providerName, providerUri, modelId);
    }

    /// <inheritdoc />
    public async Task<ImageGenerationResponse> GenerateAsync(
        Microsoft.Extensions.AI.ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var operation = await _client.SubmitAsync(
            ToBaizeRequest(request, options),
            cancellationToken).ConfigureAwait(false);

        if (operation.State != GenerationOperationState.Succeeded)
        {
            throw operation.Error is { } error
                ? new BaizeException(
                    error.Message ?? "Image generation failed.",
                    error.Kind,
                    providerStatus: error.ProviderStatus)
                : new BaizeException(
                    "Image generation did not complete successfully.",
                    GenerationErrorKind.GenerationFailed);
        }

        var contents = new List<AIContent>();
        if (operation.Result is { } result)
        {
            foreach (var asset in result.Assets)
            {
                var content = ToAIContent(asset);
                if (content is not null)
                    contents.Add(content);
            }
        }

        return new ImageGenerationResponse(contents)
        {
            RawRepresentation = operation
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        if (serviceType.IsInstanceOfType(this))
            return this;
        if (serviceType == typeof(ImageGeneratorMetadata))
            return _metadata;
        if (serviceType.IsInstanceOfType(_client))
            return _client;
        return null;
    }

    /// <summary>The adapter owns no disposable provider resources.</summary>
    public void Dispose() { }

    private Penghou.Baize.Generation.ImageGenerationRequest ToBaizeRequest(
        Microsoft.Extensions.AI.ImageGenerationRequest source,
        ImageGenerationOptions? options)
    {
        var inputs = new List<LlmMediaSource>();
        if (source.OriginalImages is not null)
        {
            foreach (var image in source.OriginalImages)
                inputs.Add(ToMediaSource(image));
        }

        return new Penghou.Baize.Generation.ImageGenerationRequest
        {
            Prompt = source.Prompt ?? string.Empty,
            Inputs = inputs,
            Count = options?.Count ?? 1,
            Size = options?.ImageSize is { } size
                ? new GenerationImageSize(size.Width, size.Height)
                : null,
            OutputFormat = options?.MediaType
        };
    }

    private LlmMediaSource ToMediaSource(AIContent content) => content switch
    {
        DataContent data => new LlmInlineDataSource(data.Data),
        UriContent uri => new LlmUriSource(uri.Uri),
        HostedFileContent file => new LlmProviderFileSource(
            new LlmProviderKey(_providerName),
            file.FileId),
        _ => throw new NotSupportedException(
            $"Microsoft.Extensions.AI content '{content.GetType().Name}' " +
            "is not supported as an input image.")
    };

    private static AIContent? ToAIContent(GeneratedAsset asset) => asset.Source switch
    {
        InlineGeneratedAssetSource inline => new DataContent(
            inline.Data,
            asset.ContentType ?? inline.ContentType ?? "application/octet-stream"),
        UriGeneratedAssetSource uri => new UriContent(
            uri.Uri,
            asset.ContentType ?? "application/octet-stream"),
        ProviderGeneratedAssetSource file => new HostedFileContent(file.ProviderFileId),
        _ => null
    };
}
