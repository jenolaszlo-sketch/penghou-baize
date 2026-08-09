namespace Penghou.Baize;

/// <summary>The result of executing a tool call.</summary>
/// <param name="ToolCallId">The identifier of the tool call this result answers.</param>
/// <param name="ToolName">The name of the tool that was executed.</param>
/// <param name="Content">The result content to feed back to the model.</param>
/// <param name="Succeeded">Whether the tool executed successfully.</param>
public sealed record LlmToolResult(
    string ToolCallId,
    string ToolName,
    string Content,
    bool Succeeded = true);
