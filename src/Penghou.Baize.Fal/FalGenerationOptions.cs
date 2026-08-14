using Penghou.Baize.Generation;

namespace Penghou.Baize.Fal;

/// <summary>
/// Configuration for a fal.ai queue generation endpoint. One options instance
/// maps to one <c>IGenerationClient</c> endpoint; multiple endpoints register
/// under distinct identifiers. fal.ai queues arbitrary per-model JSON inputs, so
/// the Baize endpoint is model-agnostic: capabilities describe what the
/// configured model supports, and the payload is built from the common request.
/// </summary>
public sealed class FalGenerationOptions
{
    /// <summary>
    /// The fal.ai queue API base URL. Defaults to <c>https://queue.fal.run</c>.
    /// </summary>
    public string BaseUrl { get; set; } = "https://queue.fal.run";

    /// <summary>The fal.ai API secret.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The model identifier posted to the queue, for example
    /// <c>fal-ai/flux/dev</c>. Unlike Runway, fal queues a generic JSON input, so
    /// the model determines the accepted schema.
    /// </summary>
    public string Model { get; set; } = "fal-ai/flux/dev";

    /// <summary>
    /// The generation features the endpoint advertises for the configured model.
    /// Conservative by default; only the listed features validate and route to
    /// wire endpoints. fal's queue itself is modality-agnostic, so the supported
    /// features depend entirely on the model.
    /// </summary>
    public GenerationFeature Features { get; set; } =
        GenerationFeature.TextToImage |
        GenerationFeature.ImageToImage |
        GenerationFeature.OperationRetrieval |
        GenerationFeature.Cancellation;
}