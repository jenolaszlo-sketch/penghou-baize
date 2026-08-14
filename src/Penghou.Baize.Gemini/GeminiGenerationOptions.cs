using Penghou.Baize.Generation;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Configuration for a Gemini generation endpoint. One options instance maps
/// to one <c>IGenerationClient</c> endpoint; multiple endpoints register under
/// distinct identifiers.
/// </summary>
public sealed class GeminiGenerationOptions
{
    /// <summary>API base URL; <c>v1beta</c> is appended when not already present.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>The Gemini API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The image-capable Gemini model identifier (for example <c>gemini-3.1-flash-lite-image</c>).</summary>
    public string Model { get; set; } = "gemini-3.1-flash-lite-image";

    /// <summary>The default requested output image size (for example <c>1K</c>, <c>2K</c>, <c>4K</c>).</summary>
    public string? ImageSize { get; set; }

    /// <summary>
    /// The MIME type assumed for inline image inputs that carry no content type.
    /// </summary>
    public string DefaultInputImageMimeType { get; set; } = "image/png";

    /// <summary>Whether responses are stored on the provider for later retrieval.</summary>
    public bool StoreResponses { get; set; }

    /// <summary>
    /// The generation features the endpoint advertises. Conservative by default;
    /// only the listed features validate and are routed to wire endpoints.
    /// </summary>
    public GenerationFeature Features { get; set; } =
        GenerationFeature.TextToImage |
        GenerationFeature.ImageToImage;
}
