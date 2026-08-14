namespace Penghou.Baize.Generation;

/// <summary>
/// An image artifact-generation request.
/// <list type="bullet">
/// <item>Prompt only → text-to-image.</item>
/// <item>Prompt + image inputs → image-to-image / edit.</item>
/// <item>Prompt + references → reference-conditioned generation.</item>
/// </list>
/// </summary>
public sealed record ImageGenerationRequest : GenerationRequest
{
    /// <summary>The generation prompt.</summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Input image assets. When present the request is treated as an image
    /// edit / reference-conditioned generation.
    /// </summary>
    public IReadOnlyList<LlmMediaSource> Inputs { get; init; } = [];

    /// <summary>The number of candidate images to request. Defaults to one.</summary>
    public int Count { get; init; } = 1;

    /// <summary>The requested aspect ratio (for example <c>16:9</c>), when the endpoint accepts one.</summary>
    public string? AspectRatio { get; init; }

    /// <summary>The requested image size in pixels, when the endpoint accepts one.</summary>
    public GenerationImageSize? Size { get; init; }

    /// <summary>The requested output format (for example <c>png</c> or <c>image/png</c>).</summary>
    public string? OutputFormat { get; init; }

    /// <summary>A deterministic seed, when the endpoint accepts one.</summary>
    public int? Seed { get; init; }
}
