using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;

namespace Penghou.Baize.OpenAi.Tests;

/// <summary>Runs the shared conformance suite against the OpenAI generation client.</summary>
public sealed class OpenAiGenerationClientContractTests : GenerationClientContractTests
{
    protected override string ProviderName => "OpenAi";

    protected override string EndpointId => "openai-gen-1";

    protected override GenerationCapabilities ImageOnlyCapabilities { get; } = new()
    {
        Features = GenerationFeature.TextToImage |
                   GenerationFeature.ImageToImage |
                   GenerationFeature.MultipleCandidates,
        InputTransports = new HashSet<LlmContentTransport>
        {
            LlmContentTransport.Uri,
            LlmContentTransport.InlineData
        },
        MaximumCandidates = 10
    };

    protected override bool SupportsMultipleCandidates => true;

    protected override IGenerationClient CreateClient(RecordingHandler handler) =>
        new OpenAiGenerationClient(
            "gpt-image-1",
            new TestHttpClientFactory(new HttpClient(handler)),
            apiKey: "secret",
            new Uri("https://openai.test/v1"),
            ImageOnlyCapabilities,
            EndpointId);

    protected override ImageGenerationRequest CreateImageRequest(int count = 1) =>
        new() { Prompt = "a red circle", Count = count };

    protected override string SuccessImageSubmitPayload =>
        """{"created":123,"data":[{"b64_json":"aGVsbG8="}]}""";

    protected override string SuccessImageSubmitPayloadMultiple =>
        """{"created":123,"data":[{"b64_json":"aGVsbG8="},{"url":"https://openai.test/2.png"}]}""";

    protected override string FailureSubmitBody(int statusCode) =>
        """{"error":{"message":"boom","type":"invalid_request_error"}}""";
}