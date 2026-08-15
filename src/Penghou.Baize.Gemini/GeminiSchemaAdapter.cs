using System.Text.Json;
using System.Text.Json.Nodes;
using Penghou.Baize;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Converts canonical JSON Schema into the subset accepted by Gemini's native API.
/// The canonical schema remains unchanged and can still be used for strict local validation.
/// </summary>
public sealed class GeminiSchemaAdapter : ILlmSchemaAdapter
{
    private const string UnsupportedKeywordReason =
        "Gemini's native schema dialect rejects the additionalProperties keyword.";

    private static readonly string[] SchemaMapKeywords =
    [
        "properties",
        "patternProperties",
        "$defs",
        "definitions",
        "dependentSchemas"
    ];

    private static readonly string[] SingleSchemaKeywords =
    [
        "items",
        "not",
        "if",
        "then",
        "else",
        "contains",
        "propertyNames",
        "unevaluatedItems",
        "unevaluatedProperties"
    ];

    private static readonly string[] SchemaArrayKeywords =
    [
        "prefixItems",
        "allOf",
        "anyOf",
        "oneOf"
    ];

    /// <summary>Shared stateless adapter instance.</summary>
    public static GeminiSchemaAdapter Default { get; } = new();

    /// <inheritdoc />
    public LlmSchemaAdaptationResult Adapt(
        JsonElement schema,
        LlmSchemaAdaptationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var root = JsonNode.Parse(schema.GetRawText())
            ?? throw new ArgumentException("Schema cannot be JSON null.", nameof(schema));
        var adaptations = new List<LlmSchemaAdaptation>();

        root = GeminiSchemaReferenceExpander.Expand(root, adaptations);

        VisitSchema(root, "$", adaptations);

        return new LlmSchemaAdaptationResult(
            JsonSerializer.SerializeToElement(root),
            adaptations);
    }

    private static void VisitSchema(
        JsonNode? node,
        string path,
        ICollection<LlmSchemaAdaptation> adaptations)
    {
        if (node is not JsonObject schema)
            return;

        if (schema.Remove("additionalProperties"))
        {
            adaptations.Add(
                new LlmSchemaAdaptation(
                    $"{path}.additionalProperties",
                    "additionalProperties",
                    "removed",
                    UnsupportedKeywordReason,
                    IsLossy: true));
        }

        foreach (var keyword in SchemaMapKeywords)
        {
            if (schema[keyword] is not JsonObject map)
                continue;

            foreach (var property in map)
                VisitSchema(property.Value, AppendProperty(path, keyword, property.Key), adaptations);
        }

        foreach (var keyword in SingleSchemaKeywords)
        {
            switch (schema[keyword])
            {
                case JsonObject child:
                    VisitSchema(child, $"{path}.{keyword}", adaptations);
                    break;

                // Draft-07 permits tuple validation through an items array.
                case JsonArray children:
                    for (var index = 0; index < children.Count; index++)
                    {
                        VisitSchema(
                            children[index],
                            $"{path}.{keyword}[{index}]",
                            adaptations);
                    }
                    break;
            }
        }

        foreach (var keyword in SchemaArrayKeywords)
        {
            if (schema[keyword] is not JsonArray children)
                continue;

            for (var index = 0; index < children.Count; index++)
                VisitSchema(children[index], $"{path}.{keyword}[{index}]", adaptations);
        }
    }

    private static string AppendProperty(
        string path,
        string keyword,
        string propertyName) =>
        $"{path}.{keyword}[{JsonSerializer.Serialize(propertyName)}]";
}
