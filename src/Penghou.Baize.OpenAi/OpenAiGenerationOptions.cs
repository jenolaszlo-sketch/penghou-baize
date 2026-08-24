using Penghou.Baize.Generation;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Configuration for an OpenAI generation endpoint. One options instance maps
/// to one <c>IGenerationClient</c> endpoint; multiple endpoints register under
/// distinct identifiers.
/// </summary>
public sealed class OpenAiGenerationOptions
{
    /// <summary>API base address; the provider paths are appended (for example <c>https://api.openai.com/v1</c>).</summary>
    public Uri BaseAddress { get; set; } = new("https://api.openai.com/v1");

    /// <summary>
    /// The per-model HTTP request timeout applied to submissions and status
    /// polls against this endpoint. When null the shared transport default applies.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>The OpenAI API key. Leave empty for anonymous or local endpoints.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The model identifier the endpoint is bound to.</summary>
    public string Model { get; set; } = "gpt-image-1";

    /// <summary>Model override for image generation; falls back to <see cref="Model"/>.</summary>
    public string? ImageModel { get; set; }

    /// <summary>Model override for video generation; falls back to <see cref="Model"/>.</summary>
    public string? VideoModel { get; set; }

    /// <summary>Model override for speech generation; falls back to <see cref="Model"/>.</summary>
    public string? AudioModel { get; set; }

    /// <summary>
    /// The generation features the endpoint advertises. Conservative by default;
    /// only the listed features validate and are routed to wire endpoints.
    /// </summary>
    public GenerationFeature Features { get; set; } =
        GenerationFeature.TextToImage |
        GenerationFeature.ImageToImage |
        GenerationFeature.TextToVideo |
        GenerationFeature.TextToSpeech |
        GenerationFeature.MultipleCandidates |
        GenerationFeature.OperationRetrieval |
        GenerationFeature.Cancellation |
        GenerationFeature.Progress;

    /// <summary>The maximum candidate count the endpoint accepts, when documented.</summary>
    public int? MaximumCandidates { get; set; }

    /// <summary>The default speech voice used when a request does not specify one.</summary>
    public string DefaultVoice { get; set; } = "alloy";
}