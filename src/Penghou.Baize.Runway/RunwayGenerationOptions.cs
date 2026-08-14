using Penghou.Baize.Generation;

namespace Penghou.Baize.Runway;

/// <summary>
/// Configuration for a Runway generation endpoint. One options instance maps
/// to one <c>IGenerationClient</c> endpoint; multiple endpoints register under
/// distinct identifiers.
/// </summary>
public sealed class RunwayGenerationOptions
{
    /// <summary>
    /// API base URL including the <c>v1</c> segment. Defaults to the public
    /// Runway developer API.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.dev.runwayml.com/v1";

    /// <summary>The Runway API secret.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The video-generation model identifier (for example <c>gen4.5</c>).</summary>
    public string Model { get; set; } = "gen4.5";

    /// <summary>The dated API version header sent on every request.</summary>
    public string ApiVersion { get; set; } = "2024-11-06";

    /// <summary>
    /// The default output aspect ratio (for example <c>1280:720</c>) used when a
    /// request does not specify one.
    /// </summary>
    public string? DefaultRatio { get; set; } = "1280:720";

    /// <summary>
    /// The default container/encoding of the output (for example <c>mp4</c>).
    /// Omitted when null, which lets the provider default.
    /// </summary>
    public string? DefaultOutputFormat { get; set; }

    /// <summary>
    /// The MIME type assumed for inline image inputs that carry no content type.
    /// </summary>
    public string DefaultInputImageMimeType { get; set; } = "image/png";

    /// <summary>
    /// The generation features the endpoint advertises. Conservative by default;
    /// only the listed features validate and are routed to wire endpoints.
    /// </summary>
    public GenerationFeature Features { get; set; } =
        GenerationFeature.TextToVideo |
        GenerationFeature.ImageToVideo |
        GenerationFeature.OperationRetrieval |
        GenerationFeature.Cancellation |
        GenerationFeature.Progress;
}