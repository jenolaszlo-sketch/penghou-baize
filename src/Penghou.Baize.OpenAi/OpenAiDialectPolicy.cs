using Penghou.Baize;

namespace Penghou.Baize.OpenAi;

/// <summary>Applies capabilities implied by a selected OpenAI wire dialect.</summary>
internal static class OpenAiDialectPolicy
{
    /// <summary>Returns the effective capabilities for the dialect.</summary>
    public static LlmEndpointCapabilities Apply(
        LlmEndpointCapabilities capabilities,
        OpenAiDialect dialect) =>
        dialect == OpenAiDialect.DeepSeek
            ? capabilities with
            {
                ParallelToolCalls = true,
                NativeStructuredOutput = false,
                StructuredOutputViaTool = true,
                Thinking = true,
                ThinkingDisable = true
            }
            : capabilities with { ThinkingDisable = false };
}
