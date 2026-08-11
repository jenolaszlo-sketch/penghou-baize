namespace Penghou.Baize;

/// <summary>
/// Requests the model to produce output in a particular format.
/// </summary>
public sealed class LlmResponseFormat
{
    /// <summary>The format type (for example "json_schema").</summary>
    public string Type { get; }

    /// <summary>The JSON schema the output must match, when applicable.</summary>
    public string? Schema { get; }

    private LlmResponseFormat(string type, string? schema)
    {
        Type = type;
        Schema = schema;
    }

    /// <summary>Requests JSON output matching the given JSON schema.</summary>
    /// <param name="schemaJson">The JSON schema the output must match.</param>
    /// <returns>A response format requesting schema-compliant JSON.</returns>
    public static LlmResponseFormat JsonSchema(string schemaJson) =>
        new("json_schema", schemaJson);

    /// <summary>Requests valid JSON without constraining it to a schema.</summary>
    public static LlmResponseFormat Json() => new("json_object", null);
}
