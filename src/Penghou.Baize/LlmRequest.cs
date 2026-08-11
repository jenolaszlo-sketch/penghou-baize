namespace Penghou.Baize;

/// <summary>A canonical chat completion request.</summary>
public sealed record LlmRequest
{
    private readonly IReadOnlyList<LlmMessage> _messages;
    private readonly double? _temperature;
    private readonly int? _maxTokens;
    private readonly IReadOnlyList<LlmTool> _tools;
    private readonly LlmResponseFormat? _responseFormat;
    private readonly LlmThinkingConfig? _thinkingConfig;
    private readonly IReadOnlyDictionary<string, object?> _metadata;

    /// <summary>Initializes a request.</summary>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="temperature">Sampling temperature, when specified.</param>
    /// <param name="maxTokens">Maximum tokens to generate, when specified.</param>
    /// <param name="tools">Tools available to the model, when any.</param>
    /// <param name="responseFormat">A requested response format, when any.</param>
    /// <param name="thinkingConfig">Extended-thinking configuration, when any.</param>
    /// <param name="metadata">
    /// Host-neutral application context made available to routing and
    /// decorators. Provider clients must not serialize it onto wire requests.
    /// </param>
    public LlmRequest(IReadOnlyList<LlmMessage> messages,
        double? temperature = null,
        int? maxTokens = null,
        IList<LlmTool>? tools = null,
        LlmResponseFormat? responseFormat = null,
        LlmThinkingConfig? thinkingConfig = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = messages.ToArray();
        _temperature = temperature;
        _maxTokens = maxTokens;
        _tools = tools?.ToArray() ?? [];
        _responseFormat = responseFormat;
        _thinkingConfig = thinkingConfig;
        _metadata = metadata is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(metadata, StringComparer.Ordinal);
    }

    /// <summary>The conversation messages.</summary>
    public IReadOnlyList<LlmMessage> Messages => _messages;

    /// <summary>Sampling temperature, when specified.</summary>
    public double? Temperature => _temperature;

    /// <summary>Maximum tokens to generate, when specified.</summary>
    public int? MaxTokens => _maxTokens;

    /// <summary>Tools available to the model, when any.</summary>
    public IReadOnlyList<LlmTool> Tools => _tools;

    /// <summary>A requested response format, when any.</summary>
    public LlmResponseFormat? ResponseFormat => _responseFormat;

    /// <summary>Extended-thinking configuration, when any.</summary>
    public LlmThinkingConfig? ThinkingConfig => _thinkingConfig;

    /// <summary>
    /// Host-neutral application context for routing, decorators, telemetry
    /// enrichment, and cost attribution. It is not provider request data and
    /// must not contain secrets. Reusable libraries should namespace keys.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata => _metadata;
}
