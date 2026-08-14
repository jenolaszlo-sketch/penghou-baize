namespace Penghou.Baize.Generation;

/// <summary>
/// A video artifact-generation request.
/// <list type="bullet">
/// <item>Prompt only → text-to-video.</item>
/// <item>Prompt + first frame → image-to-video.</item>
/// <item>Prompt + first/last frame → interpolation where supported.</item>
/// <item>Prompt + source video → video-to-video / edit.</item>
/// </list>
/// </summary>
public sealed record VideoGenerationRequest : GenerationRequest
{
    /// <summary>The generation prompt.</summary>
    public required string Prompt { get; init; }

    /// <summary>A source video asset for video-to-video / edit.</summary>
    public LlmMediaSource? SourceVideo { get; init; }

    /// <summary>A first-frame image asset for image-to-video.</summary>
    public LlmMediaSource? FirstFrame { get; init; }

    /// <summary>A last-frame image asset for interpolation, where supported.</summary>
    public LlmMediaSource? LastFrame { get; init; }

    /// <summary>Additional reference assets for conditioning, where supported.</summary>
    public IReadOnlyList<LlmMediaSource> References { get; init; } = [];

    /// <summary>The requested clip duration, when the endpoint accepts one.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>The requested aspect ratio (for example <c>16:9</c>), when the endpoint accepts one.</summary>
    public string? AspectRatio { get; init; }

    /// <summary>The requested video size in pixels, when the endpoint accepts one.</summary>
    public GenerationVideoSize? Size { get; init; }

    /// <summary>Whether the endpoint should generate audio for the clip, when it supports it.</summary>
    public bool? GenerateAudio { get; init; }

    /// <summary>A deterministic seed, when the endpoint accepts one.</summary>
    public int? Seed { get; init; }
}
