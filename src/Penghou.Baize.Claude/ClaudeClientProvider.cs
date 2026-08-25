namespace Penghou.Baize.Claude;

/// <summary>Constructs Anthropic Claude clients for the router provider registry.</summary>
public sealed class ClaudeClientProvider : ILlmClientProvider
{
    /// <inheritdoc />
    public LlmProviderKey Key { get; } = new("Claude");

    /// <inheritdoc />
    public string DefaultBaseUrl => "https://api.anthropic.com";

    /// <inheritdoc />
    public LlmEndpointCapabilities DefaultCapabilities { get; } = new()
    {
        NativeToolCalling = true,
        ParallelToolCalls = true,
        NativeStructuredOutput = false,
        StructuredOutputViaTool = true,
        Thinking = true,
        ThinkingDisable = true,
        StreamingToolCallArguments = true,
        SupportedThinkingEfforts =
            new HashSet<LlmThinkingEffort>
            {
                LlmThinkingEffort.Low,
                LlmThinkingEffort.Medium,
                LlmThinkingEffort.High
            },
        Batch =
            BatchCapabilities.NativeBatch |
            BatchCapabilities.Polling |
            BatchCapabilities.Cancellation
    };

    /// <inheritdoc />
    public ILlmClient CreateClient(LlmClientProviderContext context)
    {
        var thinkingStyle = ResolveThinkingStyle(context);

        return new ClaudeChatClient(
            context.HttpClientFactory,
            context.Model,
            context.ApiKey,
            context.BaseUrl,
            context.Capabilities,
            thinkingStyle);
    }

    /// <inheritdoc />
    public IBaizeBatchClient? CreateBatchClient(LlmClientProviderContext context)
    {
        if (!context.Capabilities.Batch.HasFlag(BatchCapabilities.NativeBatch))
            return null;

        var thinkingStyle = ResolveThinkingStyle(context);

        return new ClaudeBatchClient(
            context.HttpClientFactory,
            context.Model,
            context.ApiKey,
            context.BaseUrl,
            context.Capabilities,
            thinkingStyle);
    }

    private static ClaudeThinkingStyle ResolveThinkingStyle(
        LlmClientProviderContext context)
    {
        var thinkingStyle = ClaudeThinkingStyle.Adaptive;

        if (context.Settings.TryGetValue(LlmSettingNames.ThinkingStyle, out var configured) &&
            !Enum.TryParse(configured, ignoreCase: true, out thinkingStyle))
        {
            throw new InvalidOperationException(
                $"Unknown Claude thinking style '{configured}' for model '{context.Model}'.");
        }

        return thinkingStyle;
    }
}
