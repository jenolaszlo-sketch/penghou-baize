namespace Penghou.Baize.Generation;

/// <summary>An audio artifact-generation request.</summary>
public sealed record AudioGenerationRequest : GenerationRequest
{
    /// <summary>The generation prompt, transcript, or description.</summary>
    public required string Prompt { get; init; }

    /// <summary>The requested audio kind. Defaults to <see cref="AudioGenerationKind.Speech"/>.</summary>
    public AudioGenerationKind Kind { get; init; } = AudioGenerationKind.Speech;

    /// <summary>A source audio asset for transformation.</summary>
    public LlmMediaSource? SourceAudio { get; init; }

    /// <summary>A provider-specific voice identifier, when the endpoint accepts one.</summary>
    public string? Voice { get; init; }

    /// <summary>The requested output format (for example <c>mp3</c> or <c>audio/mpeg</c>).</summary>
    public string? OutputFormat { get; init; }

    /// <summary>The requested duration, when the endpoint accepts one.</summary>
    public TimeSpan? Duration { get; init; }
}
