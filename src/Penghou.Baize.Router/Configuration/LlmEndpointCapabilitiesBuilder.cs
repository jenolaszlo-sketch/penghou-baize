namespace Penghou.Baize.Router.Configuration;

/// <summary>Builds explicit capability overrides for a profile or endpoint.</summary>
public sealed class LlmEndpointCapabilitiesBuilder
{
    private bool? _tools;
    private bool? _parallelTools;
    private bool? _toolsWithStructuredOutput;
    private bool? _structuredOutput;
    private bool? _structuredOutputViaTool;
    private bool? _thinking;
    private bool? _thinkingDisable;
    private bool? _streamingToolArguments;
    private IReadOnlyList<LlmThinkingEffort>? _thinkingEfforts;
    private int? _thinkingBudget;
    private IReadOnlyList<LlmContentType>? _contentTypes;
    private Dictionary<LlmContentType, LlmContentTransport>? _contentTransports;
    private BatchCapabilities? _batch;

    /// <summary>Declares native tool-call support and related combinations.</summary>
    public LlmEndpointCapabilitiesBuilder SupportsTools(
        bool enabled = true,
        bool parallel = false,
        bool withStructuredOutput = false)
    {
        _tools = enabled;
        _parallelTools = enabled && parallel;
        _toolsWithStructuredOutput = enabled && withStructuredOutput;
        return this;
    }

    /// <summary>Declares structured-output support.</summary>
    public LlmEndpointCapabilitiesBuilder SupportsStructuredOutput(
        bool native = true,
        bool viaTool = false)
    {
        _structuredOutput = native;
        _structuredOutputViaTool = viaTool;
        return this;
    }

    /// <summary>Declares extended-thinking support and accepted effort levels.</summary>
    public LlmEndpointCapabilitiesBuilder SupportsThinking(
        bool enabled = true,
        bool canDisable = true,
        int? tokenBudget = null,
        params LlmThinkingEffort[] efforts)
    {
        _thinking = enabled;
        _thinkingDisable = enabled && canDisable;
        _thinkingBudget = tokenBudget;
        _thinkingEfforts = efforts.Length == 0 ? null : efforts;
        return this;
    }

    /// <summary>Declares whether tool-call arguments are streamed incrementally.</summary>
    public LlmEndpointCapabilitiesBuilder StreamsToolCallArguments(bool enabled = true)
    { _streamingToolArguments = enabled; return this; }

    /// <summary>Adds an accepted message content type and its transports.</summary>
    public LlmEndpointCapabilitiesBuilder SupportsContent(
        LlmContentType type,
        LlmContentTransport transports = default)
    {
        var types = _contentTypes?.ToList() ?? [LlmContentType.Text];
        if (!types.Contains(type)) types.Add(type);
        _contentTypes = types;
        if (type != LlmContentType.Text)
        {
            _contentTransports ??= [];
            _contentTransports[type] = transports;
        }
        return this;
    }

    /// <summary>Declares native asynchronous batch capabilities.</summary>
    public LlmEndpointCapabilitiesBuilder SupportsBatch(BatchCapabilities capabilities)
    { _batch = capabilities; return this; }

    internal LlmEndpointCapabilitiesOptions Build() => new()
    {
        NativeToolCalling = _tools,
        ParallelToolCalls = _parallelTools,
        ToolsWithStructuredOutput = _toolsWithStructuredOutput,
        NativeStructuredOutput = _structuredOutput,
        StructuredOutputViaTool = _structuredOutputViaTool,
        Thinking = _thinking,
        ThinkingDisable = _thinkingDisable,
        StreamingToolCallArguments = _streamingToolArguments,
        SupportedThinkingEfforts = _thinkingEfforts,
        ThinkingBudget = _thinkingBudget,
        ContentTypes = _contentTypes,
        ContentTransports = _contentTransports,
        Batch = _batch
    };
}
