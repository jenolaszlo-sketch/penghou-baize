namespace Penghou.Baize.OpenAi;

/// <summary>Constructs OpenAI-compatible clients for the router provider registry.</summary>
public sealed class OpenAiClientProvider : ILlmClientProvider
{
    /// <inheritdoc />
    public LlmProviderKey Key { get; } = new("OpenAi");

    /// <inheritdoc />
    public string DefaultBaseUrl => "https://api.openai.com/v1";

    /// <inheritdoc />
    public LlmEndpointCapabilities DefaultCapabilities { get; } = new()
    {
        NativeToolCalling = true,
        ParallelToolCalls = false,
        NativeStructuredOutput = false,
        StructuredOutputViaTool = false,
        Thinking = false,
        ThinkingDisable = false,
        StreamingToolCallArguments = true,
        SupportedThinkingEfforts =
            new HashSet<LlmThinkingEffort>
            {
                LlmThinkingEffort.Low,
                LlmThinkingEffort.Medium,
                LlmThinkingEffort.High
            }
    };

    /// <inheritdoc />
    public ILlmClient CreateClient(LlmClientProviderContext context)
    {
        var dialect = OpenAiDialect.Standard;

        if (context.Settings.TryGetValue("Dialect", out var configured) &&
            !Enum.TryParse(configured, ignoreCase: true, out dialect))
        {
            throw new InvalidOperationException(
                $"Unknown OpenAI dialect '{configured}' for model '{context.Model}'.");
        }

        return new OpenAiChatClient(
            context.Model,
            context.HttpClientFactory,
            context.ApiKey,
            context.BaseUrl,
            context.Capabilities,
            dialect);
    }
}
