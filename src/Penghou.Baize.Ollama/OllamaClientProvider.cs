namespace Penghou.Baize.Ollama;

/// <summary>Constructs native Ollama clients for the router provider registry.</summary>
public sealed class OllamaClientProvider : ILlmClientProvider
{
    /// <inheritdoc />
    public LlmProviderKey Key { get; } = new("Ollama");

    /// <inheritdoc />
    public string DefaultBaseUrl => "http://localhost:11434";

    /// <inheritdoc />
    public LlmEndpointCapabilities DefaultCapabilities { get; } = new()
    {
        NativeToolCalling = false,
        ParallelToolCalls = false,
        NativeStructuredOutput = false,
        StructuredOutputViaTool = false,
        Thinking = false,
        ThinkingDisable = false,
        StreamingToolCallArguments = false,
        SupportedThinkingEfforts = new HashSet<LlmThinkingEffort>()
    };

    /// <inheritdoc />
    public ILlmClient CreateClient(LlmClientProviderContext context) =>
        new OllamaChatClient(
            context.Model,
            context.HttpClientFactory,
            context.ApiKey,
            context.BaseUrl,
            context.Capabilities);
}
