namespace Penghou.Baize;

/// <summary>
/// Default <see cref="ILlmPromptBuilder"/> that forwards the configured
/// messages, temperature, token limit, tools, response format, thinking, and metadata
/// settings into a request. Tools and structured output are deliberately
/// mutually exclusive because providers express that combination differently.
/// </summary>
public sealed class LlmPromptBuilder : ILlmPromptBuilder
{
    /// <summary>The conversation messages.</summary>
    public IReadOnlyList<LlmMessage> Messages { get; set; } = [];

    /// <summary>Sampling temperature, when set.</summary>
    public double? Temperature { get; set; }

    /// <summary>Maximum tokens to generate, when set.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Tools available to the model.</summary>
    public IList<LlmTool> Tools { get; set; } = [];

    /// <summary>A requested response format, when set.</summary>
    public LlmResponseFormat? ResponseFormat { get; set; }

    /// <summary>Extended-thinking configuration, when set.</summary>
    public LlmThinkingConfig? ThinkingConfig { get; set; }

    /// <summary>
    /// Host-neutral application context passed to routing and decorators, but
    /// never serialized by provider clients.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; set; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Builds a request for the given <paramref name="strategy"/>. The strategy
    /// is a routing hint only; endpoint capability validation decides which
    /// feature combinations can be transmitted.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <returns>The built request.</returns>
    public LlmRequest Build(ModelStrategy strategy)
    {
        return new LlmRequest(
            Messages,
            Temperature,
            MaxTokens,
            tools: Tools,
            ResponseFormat,
            ThinkingConfig,
            Metadata);
    }
}
