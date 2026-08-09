using Penghou.Baize;

namespace Penghou.Baize.Tools;

/// <summary>
/// Parses a model response into a strongly typed tool result, applying the
/// tool's JSON Schema before deserializing.
/// </summary>
/// <typeparam name="TResult">The deserialized result type.</typeparam>
public interface ILlmToolResultParser<TResult>
{
    /// <summary>
    /// Parses the tool call matching this parser's tool name from the
    /// <paramref name="response"/>.
    /// </summary>
    /// <param name="response">The model response to parse.</param>
    /// <returns>
    /// A <see cref="ToolCallParseResult{TResult}"/> that is either successful
    /// with the deserialized value, or failed with a
    /// <see cref="ToolCallParseFailure"/> and diagnostic detail.
    /// </returns>
    ToolCallParseResult<TResult> Parse(LlmResponse response);
}