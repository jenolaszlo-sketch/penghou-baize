namespace Penghou.Baize;

/// <summary>Identifies how a JSON Schema is used on a provider wire API.</summary>
public enum LlmSchemaPurpose
{
    /// <summary>The schema describes arguments accepted by a tool.</summary>
    ToolInput,

    /// <summary>The schema constrains a structured model response.</summary>
    StructuredResponse
}
