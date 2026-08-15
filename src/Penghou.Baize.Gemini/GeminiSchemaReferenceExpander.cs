using System.Text.Json;
using System.Text.Json.Nodes;
using Penghou.Baize;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Inlines local JSON Schema references into an owned provider-wire schema.
/// Gemini's native function-declaration dialect rejects <c>$ref</c>, while the
/// canonical schema remains authoritative for local validation.
/// </summary>
internal static class GeminiSchemaReferenceExpander
{
    private static readonly string[] SchemaMapKeywords =
    [
        "properties",
        "patternProperties",
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

    public static JsonNode Expand(
        JsonNode root,
        ICollection<LlmSchemaAdaptation> adaptations) =>
        new ExpansionState(root, adaptations).Expand(root, "$", []);

    private sealed class ExpansionState(
        JsonNode root,
        ICollection<LlmSchemaAdaptation> adaptations)
    {
        public JsonNode Expand(
            JsonNode node,
            string path,
            HashSet<string> referenceStack)
        {
            if (node is not JsonObject schema)
                return node.DeepClone();

            if (schema["$ref"] is JsonValue referenceNode &&
                referenceNode.TryGetValue<string>(out var reference))
            {
                return ExpandReference(
                    schema,
                    reference,
                    path,
                    referenceStack);
            }

            var result = (JsonObject)schema.DeepClone();
            ExpandChildren(result, path, referenceStack);
            RemoveDefinitions(result, path);
            return result;
        }

        private JsonNode ExpandReference(
            JsonObject schema,
            string reference,
            string path,
            HashSet<string> referenceStack)
        {
            if (!referenceStack.Add(reference))
            {
                throw new LlmRequestValidationException(
                    $"Gemini cannot represent recursive JSON Schema reference " +
                    $"'{reference}' at '{path}.$ref'.");
            }

            try
            {
                var target = Resolve(reference);
                var siblings = schema
                    .Where(property => property.Key != "$ref")
                    .ToArray();

                JsonNode merged;
                if (target is JsonObject targetObject)
                {
                    var mergedObject = (JsonObject)targetObject.DeepClone();
                    foreach (var (key, value) in siblings)
                        mergedObject[key] = value?.DeepClone();
                    merged = mergedObject;
                }
                else if (siblings.Length == 0)
                {
                    merged = target.DeepClone();
                }
                else
                {
                    throw new LlmRequestValidationException(
                        $"Gemini cannot merge sibling schema keywords with " +
                        $"non-object reference '{reference}' at '{path}.$ref'.");
                }

                adaptations.Add(
                    new LlmSchemaAdaptation(
                        $"{path}.$ref",
                        "$ref",
                        "inlined",
                        "Gemini's native schema dialect does not accept JSON Schema references.",
                        IsLossy: false));

                return Expand(merged, path, referenceStack);
            }
            finally
            {
                referenceStack.Remove(reference);
            }
        }

        private void ExpandChildren(
            JsonObject schema,
            string path,
            HashSet<string> referenceStack)
        {
            foreach (var keyword in SchemaMapKeywords)
            {
                if (schema[keyword] is not JsonObject map)
                    continue;

                foreach (var (name, child) in map.ToArray())
                {
                    if (child is not null)
                    {
                        map[name] = Expand(
                            child,
                            AppendProperty(path, keyword, name),
                            referenceStack);
                    }
                }
            }

            foreach (var keyword in SingleSchemaKeywords)
            {
                switch (schema[keyword])
                {
                    case JsonObject child:
                        schema[keyword] = Expand(
                            child,
                            $"{path}.{keyword}",
                            referenceStack);
                        break;
                    case JsonArray children:
                        for (var index = 0; index < children.Count; index++)
                        {
                            if (children[index] is { } item)
                            {
                                children[index] = Expand(
                                    item,
                                    $"{path}.{keyword}[{index}]",
                                    referenceStack);
                            }
                        }
                        break;
                }
            }

            foreach (var keyword in SchemaArrayKeywords)
            {
                if (schema[keyword] is not JsonArray children)
                    continue;

                for (var index = 0; index < children.Count; index++)
                {
                    if (children[index] is { } child)
                    {
                        children[index] = Expand(
                            child,
                            $"{path}.{keyword}[{index}]",
                            referenceStack);
                    }
                }
            }
        }

        private JsonNode Resolve(string reference)
        {
            if (reference == "#")
                return root;

            if (!reference.StartsWith("#/", StringComparison.Ordinal))
            {
                throw new LlmRequestValidationException(
                    $"Gemini cannot resolve external JSON Schema reference " +
                    $"'{reference}'. Only local references are supported.");
            }

            JsonNode? current = root;
            foreach (var encodedSegment in reference[2..].Split('/'))
            {
                var segment = Uri.UnescapeDataString(encodedSegment)
                    .Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);

                current = current switch
                {
                    JsonObject obj when obj.TryGetPropertyValue(segment, out var value) => value,
                    JsonArray array when int.TryParse(segment, out var index) &&
                        index >= 0 && index < array.Count => array[index],
                    _ => null
                };

                if (current is null)
                {
                    throw new LlmRequestValidationException(
                        $"Gemini could not resolve local JSON Schema reference " +
                        $"'{reference}'.");
                }
            }

            return current;
        }

        private void RemoveDefinitions(JsonObject schema, string path)
        {
            RemoveDefinitionKeyword(schema, path, "$defs");
            RemoveDefinitionKeyword(schema, path, "definitions");
        }

        private void RemoveDefinitionKeyword(
            JsonObject schema,
            string path,
            string keyword)
        {
            if (!schema.Remove(keyword))
                return;

            adaptations.Add(
                new LlmSchemaAdaptation(
                    $"{path}.{keyword}",
                    keyword,
                    "removed",
                    "Definitions were removed after all local references were inlined.",
                    IsLossy: false));
        }
    }

    private static string AppendProperty(
        string path,
        string keyword,
        string propertyName) =>
        $"{path}.{keyword}[{JsonSerializer.Serialize(propertyName)}]";
}
