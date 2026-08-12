namespace Penghou.Baize.OpenAi;

/// <summary>Constants and predicates for tool-backed structured output.</summary>
internal static class OpenAiStructuredOutput
{
    /// <summary>The reserved synthetic function name.</summary>
    public const string ToolName = "structured_output";

    /// <summary>Returns whether the request must use the synthetic tool.</summary>
    public static bool UsesSyntheticTool(
        LlmEndpointCapabilities capabilities,
        LlmRequest request) =>
        request.ResponseFormat is { Schema: not null } &&
        !capabilities.NativeStructuredOutput &&
        capabilities.StructuredOutputViaTool;
}
