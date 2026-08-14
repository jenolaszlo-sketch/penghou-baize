namespace Penghou.Baize.Generation;

/// <summary>
/// Statically-known per-endpoint/model constraints used to validate a common
/// generation request before it is transmitted. An absent constraint means the
/// provider exposes no statically known limit.
/// </summary>
public sealed record GenerationConstraints
{
    /// <summary>The maximum number of input assets accepted by the endpoint.</summary>
    public int? MaximumInputs { get; init; }

    /// <summary>The shortest supported video or audio duration.</summary>
    public TimeSpan? MinimumDuration { get; init; }

    /// <summary>The longest supported video or audio duration.</summary>
    public TimeSpan? MaximumDuration { get; init; }

    /// <summary>
    /// The output formats the endpoint accepts. An empty set claims no
    /// statically-known format restriction.
    /// </summary>
    public IReadOnlySet<string> SupportedOutputFormats { get; init; } =
        new HashSet<string>();

    /// <summary>
    /// The image sizes the endpoint accepts. An empty set claims no
    /// statically-known size restriction.
    /// </summary>
    public IReadOnlySet<GenerationImageSize> SupportedImageSizes { get; init; } =
        new HashSet<GenerationImageSize>();

    /// <summary>
    /// The video sizes the endpoint accepts. An empty set claims no
    /// statically-known size restriction.
    /// </summary>
    public IReadOnlySet<GenerationVideoSize> SupportedVideoSizes { get; init; } =
        new HashSet<GenerationVideoSize>();

    /// <summary>
    /// The aspect ratios the endpoint accepts. An empty set claims no
    /// statically-known aspect-ratio restriction.
    /// </summary>
    public IReadOnlySet<string> SupportedAspectRatios { get; init; } =
        new HashSet<string>();

    /// <summary>
    /// The audio generation kinds the endpoint accepts. An empty set claims no
    /// statically-known kind restriction beyond the advertised features.
    /// </summary>
    public IReadOnlySet<AudioGenerationKind> SupportedAudioKinds { get; init; } =
        new HashSet<AudioGenerationKind>();
}
