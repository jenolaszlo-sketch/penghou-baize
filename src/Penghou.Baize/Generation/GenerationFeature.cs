namespace Penghou.Baize.Generation;

/// <summary>
/// Flags describing the artifact-generation operations a configured generation
/// endpoint supports. Capabilities describe the configured endpoint/model, not
/// everything a vendor has ever shipped, so a request is rejected before an
/// expensive call whenever its required feature is absent.
/// </summary>
[Flags]
public enum GenerationFeature
{
    /// <summary>No generation feature is supported.</summary>
    None = 0,

    /// <summary>Text prompt to image generation.</summary>
    TextToImage = 1 << 0,

    /// <summary>Image (or reference-conditioned) to image editing.</summary>
    ImageToImage = 1 << 1,

    /// <summary>Image upscaling / super-resolution.</summary>
    ImageUpscale = 1 << 2,

    /// <summary>Text prompt to video generation.</summary>
    TextToVideo = 1 << 3,

    /// <summary>Image (first frame) to video generation.</summary>
    ImageToVideo = 1 << 4,

    /// <summary>Video-to-video transformation / editing.</summary>
    VideoToVideo = 1 << 5,

    /// <summary>Video upscaling.</summary>
    VideoUpscale = 1 << 6,

    /// <summary>Text to speech synthesis.</summary>
    TextToSpeech = 1 << 7,

    /// <summary>Text to sound-effect generation.</summary>
    TextToSound = 1 << 8,

    /// <summary>Text to music generation.</summary>
    TextToMusic = 1 << 9,

    /// <summary>Audio transformation / editing.</summary>
    AudioTransform = 1 << 10,

    /// <summary>The endpoint accepts a candidate count greater than one.</summary>
    MultipleCandidates = 1 << 11,

    /// <summary>Submitted operations can be retrieved later by handle.</summary>
    OperationRetrieval = 1 << 12,

    /// <summary>Submitted operations can be canceled by handle.</summary>
    Cancellation = 1 << 13,

    /// <summary>The endpoint reports progress for in-flight operations.</summary>
    Progress = 1 << 14,

    /// <summary>The endpoint streams generation events.</summary>
    Streaming = 1 << 15,

    /// <summary>Submissions are idempotent and safe to retry after ambiguity.</summary>
    IdempotentSubmission = 1 << 16
}
