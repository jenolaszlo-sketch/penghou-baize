using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;

namespace Penghou.Baize.Gemini.Tests;

/// <summary>Runs the shared conformance suite against the Gemini generation client.</summary>
public sealed class GeminiGenerationClientContractTests : GenerationClientContractTests
{
    protected override string ProviderName => "Gemini";

    protected override string EndpointId => "gemini-gen-1";

    protected override GenerationCapabilities ImageOnlyCapabilities { get; } = new()
    {
        Features = GenerationFeature.TextToImage |
                   GenerationFeature.ImageToImage,
        InputTransports = new HashSet<LlmContentTransport>
        {
            LlmContentTransport.Uri,
            LlmContentTransport.InlineData
        }
    };

    protected override IGenerationClient CreateClient(RecordingHandler handler) =>
        new GeminiGenerationClient(
            "gemini-3.1-flash-lite-image",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            "https://generativelanguage.googleapis.com/v1beta",
            ImageOnlyCapabilities,
            EndpointId);

    protected override ImageGenerationRequest CreateImageRequest(int count = 1) =>
        new() { Prompt = "a red circle", Count = count };

    protected override string SuccessImageSubmitPayload =>
        """
        {
          "id": "interaction-1",
          "status": "completed",
          "output_image": {
            "type": "image",
            "mime_type": "image/png",
            "data": "aGVsbG8="
          }
        }
        """;

    protected override string FailureSubmitBody(int statusCode) =>
        """{"error":{"message":"boom","code":"invalid_request"}}""";
}
