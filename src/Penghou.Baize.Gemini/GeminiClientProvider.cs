namespace Penghou.Baize.Gemini;

/// <summary>Constructs native Gemini clients for the router provider registry.</summary>
public sealed class GeminiClientProvider : ILlmClientProvider
{
    /// <inheritdoc />
    public LlmProviderKey Key { get; } = new("Gemini");

    /// <inheritdoc />
    public string DefaultBaseUrl => "https://generativelanguage.googleapis.com";

    /// <inheritdoc />
    public LlmEndpointCapabilities DefaultCapabilities { get; } = new()
    {
        NativeToolCalling = true,
        ParallelToolCalls = true,
        NativeStructuredOutput = true,
        StructuredOutputViaTool = false,
        Thinking = true,
        ThinkingDisable = true,
        StreamingToolCallArguments = true,
        SupportedThinkingEfforts =
            new HashSet<LlmThinkingEffort>
            {
                LlmThinkingEffort.Low,
                LlmThinkingEffort.Medium,
                LlmThinkingEffort.High,
                LlmThinkingEffort.Max
            }
    };

    /// <inheritdoc />
    public ILlmClient CreateClient(LlmClientProviderContext context) =>
        new GeminiChatClient(
            context.Model,
            context.HttpClientFactory,
            context.ApiKey,
            context.BaseUrl,
            context.Capabilities);
}
