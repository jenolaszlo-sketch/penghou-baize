namespace Penghou.Baize.Tools;

/// <summary>
/// Describes why parsing a tool call failed.
/// </summary>
public enum ToolCallParseFailure
{
    /// <summary>No failure; the parse succeeded.</summary>
    None,

    /// <summary>The response contained no call for the expected tool.</summary>
    MissingToolCall,

    /// <summary>The tool call arguments were empty or whitespace.</summary>
    EmptyArguments,

    /// <summary>The tool call arguments were not valid JSON.</summary>
    InvalidJson,

    /// <summary>The tool call arguments did not satisfy the tool's JSON Schema.</summary>
    SchemaValidationFailed,

    /// <summary>The validated arguments could not be deserialized to the result type.</summary>
    DeserializationFailed
}
