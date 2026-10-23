namespace Penghou.Baize.Qualification;

internal static class QualificationFixtures
{
    public const string ExpectedBaseline = "BAIZE_OK";
    public const string ToolName = "echo_value";
    public const string ExpectedToolValue = "baize-live";
    public const string ToolSchema = """
        {
          "type": "object",
          "properties": {
            "value": { "type": "string" }
          },
          "required": ["value"],
          "additionalProperties": false
        }
        """;

    public static LlmTool CreateTool() => new(ToolName, "Echoes the supplied value.", ToolSchema);

    public static LlmRequest CreateBaselineRequest() => new(
        [new LlmMessage("user", "Reply with the exact token BAIZE_OK and no other text.")],
        temperature: 0, maxTokens: QualificationContracts.Baseline.MaximumOutputTokens);

    public static LlmRequest CreateSingleToolRequest() => new(
        [new LlmMessage("user", "Call echo_value exactly once with value baize-live. Do not answer in text.")],
        temperature: 0, maxTokens: QualificationContracts.SingleTool.MaximumOutputTokens,
        tools: [CreateTool()]);
}
