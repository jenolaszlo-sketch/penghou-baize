using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Penghou.Baize.Tools.Schema;

/// <summary>
/// Generates provider-compatible JSON Schema from C# types via reflection, using
/// System.Text.Json's built-in JsonSchemaExporter (.NET 9+). Normalizes output to
/// the narrower JSON Schema subset both Anthropic and OpenAI-style tool-calling
/// APIs accept — neither tolerates type arrays (["object","null"]) or top-level
/// anyOf/oneOf/allOf, both of which JsonSchemaExporter emits by default for
/// nullable reference/value-type properties.
/// </summary>
public static class JsonSchemaGenerator
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver() // ← explicit reflection-based resolver
    };

    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TransformSchemaNode = (context, schemaNode) =>
        {
            if (schemaNode is JsonObject obj)
            {
                NormalizeNullableType(obj);
                NormalizeNullableAnyOf(obj);
                ApplyDescription(context, obj);
            }

            return schemaNode;
        }
    };

    /// <summary>Generates a JSON Schema for TResult and returns it as a JsonNode
    /// tree — useful when you need to inspect/compose the schema further, e.g. to
    /// derive a JsonSchemaExpectation for the repair pipeline.</summary>
    public static JsonNode GenerateSchemaNode<T>() =>
        Options.GetTypeInfo(typeof(T)).GetJsonSchemaAsNode(ExporterOptions);

    /// <summary>Generates a JSON Schema for TResult as a wire-ready JSON string —
    /// used for LlmTool.InputSchemaJson / OpenAiFunctionTool.Parameters /
    /// Anthropic's input_schema.</summary>
    public static string GenerateSchemaJson<T>() =>
        GenerateSchemaNode<T>().ToJsonString();

    private static void NormalizeNullableType(JsonObject obj)
    {
        if (obj["type"] is not JsonArray typeArray)
            return;

        var nonNullTypes = typeArray
            .Where(t => t?.GetValue<string>() != "null")
            .Select(t => t!.GetValue<string>())
            .ToList();

        obj["type"] = nonNullTypes.Count switch
        {
            1 => nonNullTypes[0],
            _ => new JsonArray(nonNullTypes.Select(t => (JsonNode)t).ToArray())
        };
    }

    private static void NormalizeNullableAnyOf(JsonObject obj)
    {
        if (obj["anyOf"] is not JsonArray anyOf || anyOf.Count != 2)
            return;

        var hasNullBranch = anyOf.Any(b =>
            b is JsonObject bo && bo["type"]?.GetValue<string>() == "null");

        var resolvedBranch = anyOf
            .OfType<JsonObject>()
            .FirstOrDefault(bo => bo["type"]?.GetValue<string>() != "null");

        if (!hasNullBranch || resolvedBranch is null)
            return;

        obj.Remove("anyOf");

        foreach (var (key, value) in resolvedBranch.ToList())
        {
            obj[key] = value?.DeepClone();
        }
    }

    private static void ApplyDescription(JsonSchemaExporterContext context, JsonObject obj)
    {
        var description = context.PropertyInfo?
            .AttributeProvider?
            .GetCustomAttributes(typeof(SchemaDescriptionAttribute), inherit: true)
            .OfType<SchemaDescriptionAttribute>()
            .FirstOrDefault()?.Description;

        if (description is not null)
        {
            obj["description"] = description;
        }
    }
}