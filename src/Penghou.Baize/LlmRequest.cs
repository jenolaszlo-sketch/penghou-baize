namespace Penghou.Baize;

/// <summary>A canonical chat completion request.</summary>
public sealed record LlmRequest
{
    private readonly IReadOnlyList<LlmMessage> _messages;
    private readonly double? _temperature;
    private readonly int? _maxTokens;
    private readonly IList<LlmTool> _tools;
    private readonly LlmResponseFormat? _responseFormat;
    private readonly LlmThinkingConfig? _thinkingConfig;

    /// <summary>Initializes a request.</summary>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="temperature">Sampling temperature, when specified.</param>
    /// <param name="maxTokens">Maximum tokens to generate, when specified.</param>
    /// <param name="tools">Tools available to the model, when any.</param>
    /// <param name="responseFormat">A requested response format, when any.</param>
    /// <param name="thinkingConfig">Extended-thinking configuration, when any.</param>
    public LlmRequest(IReadOnlyList<LlmMessage> messages,
        double? temperature = null,
        int? maxTokens = null,
        IList<LlmTool>? tools = null,
        LlmResponseFormat? responseFormat = null,
        LlmThinkingConfig? thinkingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = messages;
        _temperature = temperature;
        _maxTokens = maxTokens;
        _tools = tools ?? [];
        _responseFormat = responseFormat;
        _thinkingConfig = thinkingConfig;
    }

    /// <summary>The conversation messages.</summary>
    public IReadOnlyList<LlmMessage> Messages => _messages;

    /// <summary>Sampling temperature, when specified.</summary>
    public double? Temperature => _temperature;

    /// <summary>Maximum tokens to generate, when specified.</summary>
    public int? MaxTokens => _maxTokens;

    /// <summary>Tools available to the model, when any.</summary>
    public IList<LlmTool> Tools => _tools;

    /// <summary>A requested response format, when any.</summary>
    public LlmResponseFormat? ResponseFormat => _responseFormat;

    /// <summary>Extended-thinking configuration, when any.</summary>
    public LlmThinkingConfig? ThinkingConfig => _thinkingConfig;
}
