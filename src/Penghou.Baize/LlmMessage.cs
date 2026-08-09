namespace Penghou.Baize;

/// <summary>
/// A single chat message in a conversation. The message's role identifies who
/// produced it (for example <c>system</c>, <c>user</c>, <c>assistant</c>, or
/// <c>tool</c>) and <see cref="Parts"/> holds its content blocks, so a full
/// tool-call conversation — assistant tool calls, tool results, and reasoning
/// — can be represented and replayed to a provider.
/// </summary>
/// <param name="Role">The message role (for example "system", "user", "assistant", or "tool").</param>
/// <param name="Parts">The content blocks making up the message.</param>
public sealed record LlmMessage(
    string Role,
    IReadOnlyList<LlmContentPart> Parts)
{
    /// <summary>
    /// Creates a text-only message. Equivalent to
    /// <c>new LlmMessage(role, [new LlmTextContent(content)])</c>.
    /// </summary>
    /// <param name="role">The message role.</param>
    /// <param name="content">The message text.</param>
    public LlmMessage(string role, string content)
        : this(role, [new LlmTextContent(content)])
    {
    }

    /// <summary>Creates a text-only message.</summary>
    /// <param name="role">The message role.</param>
    /// <param name="text">The message text.</param>
    /// <returns>The created message.</returns>
    public static LlmMessage Text(string role, string text) =>
        new(role, [new LlmTextContent(text)]);

    /// <summary>
    /// Creates an assistant message carrying one or more tool calls, with an
    /// optional text preamble.
    /// </summary>
    /// <param name="toolCalls">The tool calls the assistant made.</param>
    /// <param name="text">Optional text the assistant produced alongside the calls.</param>
    /// <returns>The created assistant message.</returns>
    public static LlmMessage Assistant(
        IReadOnlyList<LlmToolCall> toolCalls,
        string? text = null)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);

        var parts = new List<LlmContentPart>();

        if (text is not null)
            parts.Add(new LlmTextContent(text));

        foreach (var toolCall in toolCalls)
            parts.Add(new LlmToolCallContent(toolCall));

        return new LlmMessage("assistant", parts);
    }

    /// <summary>
    /// Creates a <c>tool</c>-role message feeding one or more tool results
    /// back to the model.
    /// </summary>
    /// <param name="results">The results of the executed tool calls.</param>
    /// <returns>The created tool message.</returns>
    public static LlmMessage ToolResults(IEnumerable<LlmToolResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return new LlmMessage(
            "tool",
            results
                .Select(result => (LlmContentPart)new LlmToolResultContent(result))
                .ToList());
    }

    /// <summary>
    /// Creates a <c>tool</c>-role message feeding a single tool result back to
    /// the model.
    /// </summary>
    /// <param name="toolCallId">The identifier of the tool call this result answers.</param>
    /// <param name="toolName">The name of the tool that was executed.</param>
    /// <param name="content">The result content to feed back to the model.</param>
    /// <param name="succeeded">Whether the tool executed successfully.</param>
    /// <returns>The created tool message.</returns>
    public static LlmMessage ToolResult(
        string toolCallId,
        string toolName,
        string content,
        bool succeeded = true) =>
        ToolResults(
            [new LlmToolResult(toolCallId, toolName, content, succeeded)]);
}
