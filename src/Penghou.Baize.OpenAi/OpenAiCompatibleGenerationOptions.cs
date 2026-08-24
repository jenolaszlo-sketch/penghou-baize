using Penghou.Baize.Generation;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Opt-in configuration for an OpenAI-compatible artifact-generation endpoint.
/// Generation is never inferred from OpenAI-compatible chat support; only the
/// explicitly configured <see cref="Features"/> are advertised and validated.
/// </summary>
public sealed class OpenAiCompatibleGenerationOptions
{
    /// <summary>API base address, including any version prefix (for example <c>http://localhost:8000/v1</c>).</summary>
    public Uri BaseAddress { get; set; } = new("http://localhost:8000/v1");

    /// <summary>
    /// The per-model HTTP request timeout applied to submissions and status
    /// polls against this endpoint. When null the shared transport default applies.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>The API key, when the endpoint requires one.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The model identifier the endpoint is bound to.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Model override for image generation; falls back to <see cref="Model"/>.</summary>
    public string? ImageModel { get; set; }

    /// <summary>
    /// The generation features the endpoint advertises. Defaults to
    /// <see cref="GenerationFeature.TextToImage"/>; an endpoint may expose
    /// <c>/v1/images/generations</c> without any OpenAI video or speech APIs.
    /// </summary>
    public GenerationFeature Features { get; set; } = GenerationFeature.TextToImage;

    /// <summary>The maximum candidate count the endpoint accepts, when documented.</summary>
    public int? MaximumCandidates { get; set; }
}