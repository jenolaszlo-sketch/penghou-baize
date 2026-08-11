using Penghou.Baize.Tools.Schema;

namespace Penghou.Baize.Tools;

/// <summary>Creates tools whose input schemas are derived from C# argument types.</summary>
public static class LlmToolFactory
{
    /// <summary>
    /// Creates a tool and generates its provider-compatible JSON Schema from
    /// <typeparamref name="TArguments"/>. The schema is cached once per closed
    /// argument type.
    /// </summary>
    /// <typeparam name="TArguments">The object shape accepted by the tool.</typeparam>
    /// <param name="name">The tool's unique name.</param>
    /// <param name="description">A natural-language description of when to use the tool.</param>
    /// <returns>A canonical tool definition ready for any Baize provider.</returns>
    public static LlmTool Create<TArguments>(string name, string description) =>
        new(name, description, SchemaCache<TArguments>.Json);

    private static class SchemaCache<TArguments>
    {
        internal static readonly string Json =
            JsonSchemaGenerator.GenerateSchemaJson<TArguments>();
    }
}
