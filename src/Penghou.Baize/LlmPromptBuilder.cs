namespace Penghou.Baize;

/// <summary>
/// Default <see cref="ILlmPromptBuilder"/> that forwards the configured
/// messages, temperature, token limit, tools, response format, and thinking
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
    /// Builds a request for the given <paramref name="strategy"/>. Throws when
    /// tools are configured for <see cref="ModelStrategy.StructuredOutput"/>,
    /// since no provider can juxtapose tool calls with a structured response
    /// format.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <returns>The built request.</returns>
    /// <exception cref="InvalidOperationException">
    /// When <paramref name="strategy"/> is <see cref="ModelStrategy.StructuredOutput"/>
    /// and <see cref="Tools"/> is not empty, or a <see cref="ResponseFormat"/> is set
    /// for any strategy other than <see cref="ModelStrategy.StructuredOutput"/>.
    /// </exception>
    public LlmRequest Build(ModelStrategy strategy)
    {
        if (strategy == ModelStrategy.StructuredOutput && Tools.Count > 0)
            throw new InvalidOperationException(
                "Tools cannot be combined with StructuredOutput: a structured response format and tool calling are mutually exclusive.");

        if (strategy != ModelStrategy.StructuredOutput && ResponseFormat is not null)
            throw new InvalidOperationException(
                $"ResponseFormat is only valid for the {nameof(ModelStrategy.StructuredOutput)} strategy.");

        return new LlmRequest(
            Messages,
            Temperature,
            MaxTokens,
            tools: Tools,
            ResponseFormat,
            ThinkingConfig);
    }
}
