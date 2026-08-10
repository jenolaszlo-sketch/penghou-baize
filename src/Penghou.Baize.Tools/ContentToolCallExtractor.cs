using Penghou.Nuwa;
using System.Text.Json;

namespace Penghou.Baize.Tools;

/// <summary>
/// Default <see cref="IContentToolCallExtractor"/> implementation. Repairs the
/// model content as a whole against a synthesized tool-call expectation, then
/// walks the result to find known tool calls (recognizing <c>name</c>,
/// <c>tool</c> and nested <c>function</c> shapes, flattened root-level
/// arguments, and calls wrapped in an array), and finally repairs each call's
/// arguments JSON against its individual tool schema.
/// </summary>
public sealed class ContentToolCallExtractor(
    IJsonRepairPipeline jsonRepairPipeline)
    : IContentToolCallExtractor
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LlmToolCall>> ExtractAsync(
        string? content,
        IReadOnlyCollection<LlmTool> tools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tools);

        if (string.IsNullOrWhiteSpace(content))
            return [];

        if (tools.Count == 0)
            return [];

        using var repairResult =
            await jsonRepairPipeline.RepairAsync(
                content,
                CreateContentExpectation(tools),
                cancellationToken);

        if (repairResult.Document is null)
            return [];

        var toolsByName = tools
            .GroupBy(
                tool => tool.Name,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        var calls = new List<LlmToolCall>();

        ExtractToolCalls(
            repairResult.Document.RootElement,
            toolsByName,
            calls,
            repairResult);

        var repaired = new List<LlmToolCall>(calls.Count);
        foreach (var call in calls)
        {
            repaired.Add(
                await RepairArgumentsAsync(
                    call,
                    toolsByName,
                    cancellationToken));
        }

        return repaired;
    }

    private async Task<LlmToolCall> RepairArgumentsAsync(
        LlmToolCall call,
        IReadOnlyDictionary<string, LlmTool> toolsByName,
        CancellationToken cancellationToken)
    {
        var expectation =
            toolsByName.TryGetValue(
                call.Name,
                out var tool)
                ? JsonSchemaExpectation.FromSchemaJson(
                    tool.InputSchemaJson)
                : null;

        using var repairResult =
            await jsonRepairPipeline.RepairAsync(
                call.ArgumentsJson,
                expectation,
                cancellationToken);
        var attempts = MergeAttempts(
            call.JsonRepairAttempts,
            RepairAttemptMapper.Combine(repairResult));

        if (repairResult.Document is null)
        {
            return call with
            {
                JsonRepairAttempts = attempts
            };
        }

        return call with
        {
            ArgumentsJson =
                repairResult.Document.RootElement.GetRawText(),
            JsonWasRepaired =
                call.JsonWasRepaired ||
                repairResult.WasRepaired,
            JsonRepairAttempts = attempts
        };
    }

    private static void ExtractToolCalls(
        JsonElement element,
        IReadOnlyDictionary<string, LlmTool> toolsByName,
        ICollection<LlmToolCall> calls,
        JsonRepairResult repairResult)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    /*
                     * Stop traversing this object after recognizing it as a tool
                     * call. This prevents duplicate extraction from nested shapes
                     * such as:
                     *
                     * {
                     *   "function": {
                     *     "name": "emit_files",
                     *     "arguments": {}
                     *   }
                     * }
                     */
                    if (TryCreateToolCall(
                        element,
                        toolsByName,
                        repairResult,
                        out var call))
                    {
                        calls.Add(call);
                        return;
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        ExtractToolCalls(
                            property.Value,
                            toolsByName,
                            calls,
                            repairResult);
                    }

                    return;
                }

            case JsonValueKind.Array:
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        ExtractToolCalls(
                            item,
                            toolsByName,
                            calls,
                            repairResult);
                    }

                    return;
                }
        }
    }

    private static bool TryCreateToolCall(
        JsonElement root,
        IReadOnlyDictionary<string, LlmTool> toolsByName,
        JsonRepairResult repairResult,
        out LlmToolCall call)
    {
        call = default!;

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        var toolName = GetToolName(root);

        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        if (!toolsByName.TryGetValue(toolName, out var tool))
            return false;

        var recoveredFlattenedArguments = false;
        string? argumentsJson;
        var recoveredPropertyCount = 0;

        if (TryGetArguments(root, out var argumentsElement))
        {
            argumentsJson = GetArgumentsJson(argumentsElement);
        }
        else if (TryRecoverFlattenedArguments(
                     root,
                     tool.InputSchemaJson,
                     out argumentsJson,
                     out recoveredPropertyCount))
        {
            recoveredFlattenedArguments = true;
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(argumentsJson))
            return false;

        var attempts = RepairAttemptMapper.Combine(repairResult);
        if (recoveredFlattenedArguments)
        {
            attempts = attempts
                .Append(new LlmRepairAttempt(
                    "schema-guided-flattened-arguments",
                    LlmRepairStatus.Succeeded,
                    Note:
                        $"recovered {recoveredPropertyCount} root-level argument property/properties using the tool schema"))
                .ToArray();
        }

        call = new LlmToolCall(
            Id: Guid.NewGuid().ToString("N"),
            Name: toolName,
            ArgumentsJson: argumentsJson,
            JsonWasRepaired:
                repairResult.WasRepaired ||
                recoveredFlattenedArguments,
            JsonRepairAttempts: attempts);

        return true;
    }

    private static bool TryRecoverFlattenedArguments(
        JsonElement root,
        string inputSchemaJson,
        out string? argumentsJson,
        out int recoveredPropertyCount)
    {
        argumentsJson = null;
        recoveredPropertyCount = 0;

        JsonDocument schemaDocument;
        try
        {
            schemaDocument = JsonDocument.Parse(inputSchemaJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (schemaDocument)
        {
            if (!schemaDocument.RootElement.TryGetProperty(
                    "properties",
                    out var schemaProperties) ||
                schemaProperties.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var propertyNames = schemaProperties
                .EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requiredNames = ReadRequiredPropertyNames(
                schemaDocument.RootElement);
            var recoveredProperties = new List<JsonProperty>();
            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var property in root.EnumerateObject())
            {
                if (IsToolNameProperty(property.Name))
                    continue;

                if (!propertyNames.Contains(property.Name) ||
                    !seen.Add(property.Name))
                {
                    return false;
                }

                recoveredProperties.Add(property);
            }

            if (recoveredProperties.Count == 0 ||
                requiredNames.Any(required =>
                    !seen.Contains(required)))
            {
                return false;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in recoveredProperties)
                    property.WriteTo(writer);
                writer.WriteEndObject();
            }

            recoveredPropertyCount = recoveredProperties.Count;
            argumentsJson = System.Text.Encoding.UTF8.GetString(
                stream.ToArray());
            return true;
        }
    }

    private static IReadOnlySet<string> ReadRequiredPropertyNames(
        JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var required) ||
            required.ValueKind != JsonValueKind.Array)
        {
            return new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        }

        return required
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsToolNameProperty(string propertyName) =>
        propertyName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("tool", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("function", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<LlmRepairAttempt>
        MergeAttempts(
            IReadOnlyList<LlmRepairAttempt>? contentAttempts,
            IReadOnlyList<LlmRepairAttempt> argumentAttempts)
    {
        var merged = new List<LlmRepairAttempt>();

        if (contentAttempts is not null)
        {
            merged.AddRange(
                contentAttempts.Select(
                    attempt =>
                        attempt with
                        {
                            Name = $"tool-call/{attempt.Name}"
                        }));
        }

        merged.AddRange(
            argumentAttempts.Select(
                attempt =>
                    attempt with
                    {
                        Name = $"arguments/{attempt.Name}"
                    }));

        return merged;
    }

    private static JsonSchemaExpectation?
        CreateContentExpectation(
            IEnumerable<LlmTool> tools)
    {
        var branches =
            new System.Text.Json.Nodes.JsonArray();
        var argumentSchemas =
            new List<
                System.Text.Json.Nodes.JsonNode>();

        foreach (var tool in tools)
        {
            System.Text.Json.Nodes.JsonNode? input;

            try
            {
                input =
                    System.Text.Json.Nodes.JsonNode.Parse(
                        tool.InputSchemaJson);
            }
            catch (JsonException)
            {
                input = null;
            }

            if (input is null)
            {
                continue;
            }

            argumentSchemas.Add(input);
            branches.Add(
                CreateToolBranch(
                    tool.Name,
                    input));
        }

        if (branches.Count == 0)
            return null;

        var argumentsSchema =
            MergeArgumentSchemas(argumentSchemas);
        var callProperties =
            CreateToolCallProperties(
                argumentsSchema);
        var rootSchema =
            new System.Text.Json.Nodes.JsonObject
            {
                // One branch per tool, discrimated by the tool name carried in
                // the call. Repairs only ever use the schema of the tool the
                // call actually names, so arguments belonging to another tool
                // are never blessed into a valid-looking call.
                ["oneOf"] = branches,
                // Canonical merged view: guides the tolerant parser's string
                // recovery when the raw call shape is ambiguous. The oneOf
                // branches above are what actually constrain repairs.
                ["type"] = "object",
                ["properties"] =
                    (System.Text.Json.Nodes.JsonObject)
                        callProperties.DeepClone(),
                // Pseudo calls are also commonly wrapped in a top-level
                // array. Supplying both shapes is intentional: this
                // expectation guides recovery and is not used to validate
                // the outer model response.
                ["items"] =
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["properties"] =
                            (System.Text.Json.Nodes.JsonObject)
                                callProperties.DeepClone()
                    }
            };

        ((System.Text.Json.Nodes.JsonObject)
            rootSchema["properties"]!)["function"] =
            new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "object",
                ["properties"] =
                    (System.Text.Json.Nodes.JsonObject)
                        callProperties.DeepClone()
            };

        return JsonSchemaExpectation.FromSchemaNode(
            rootSchema);
    }

    private static System.Text.Json.Nodes.JsonObject
        CreateToolBranch(
            string toolName,
            System.Text.Json.Nodes.JsonNode
                inputSchema)
    {
        var name =
            CreateToolNameDiscriminator(toolName);

        return new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "object",
            ["properties"] =
                new System.Text.Json.Nodes.JsonObject
                {
                    ["name"] =
                        (System.Text.Json.Nodes.JsonObject)
                            name.DeepClone(),
                    ["tool"] =
                        (System.Text.Json.Nodes.JsonObject)
                            name.DeepClone(),
                    ["arguments"] =
                        inputSchema.DeepClone(),
                    ["parameters"] =
                        inputSchema.DeepClone(),
                    ["function"] =
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] =
                                new System.Text.Json.Nodes.JsonObject
                                {
                                    ["name"] =
                                        (System.Text.Json.Nodes.JsonObject)
                                            name.DeepClone(),
                                    ["arguments"] =
                                        inputSchema.DeepClone()
                                }
                        }
                },
            ["required"] =
                new System.Text.Json.Nodes.JsonArray(
                    "name",
                    "arguments")
        };
    }

    private static System.Text.Json.Nodes.JsonObject
        CreateToolNameDiscriminator(
            string toolName) =>
        new()
        {
            ["type"] = "string",
            ["const"] = toolName
        };

    private static System.Text.Json.Nodes.JsonObject
        CreateToolCallProperties(
            System.Text.Json.Nodes.JsonNode
                argumentsSchema) =>
        new()
        {
            ["name"] =
                new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "string"
                },
            ["tool"] =
                new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "string"
                },
            ["arguments"] =
                argumentsSchema.DeepClone(),
            ["parameters"] =
                argumentsSchema.DeepClone()
        };

    private static System.Text.Json.Nodes.JsonNode
        MergeArgumentSchemas(
            IReadOnlyList<
                System.Text.Json.Nodes.JsonNode>
                schemas)
    {
        if (schemas.Count == 1)
            return schemas[0].DeepClone();

        var properties =
            new System.Text.Json.Nodes.JsonObject();

        foreach (var schema in schemas)
        {
            if (schema["properties"] is not
                System.Text.Json.Nodes.JsonObject
                schemaProperties)
            {
                continue;
            }

            foreach (var property in
                     schemaProperties)
            {
                if (!properties.ContainsKey(
                        property.Key))
                {
                    properties[property.Key] =
                        property.Value?.DeepClone();
                }
            }
        }

        return new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
    }

    private static string? GetArgumentsJson(
        JsonElement argumentsElement)
    {
        return argumentsElement.ValueKind switch
        {
            JsonValueKind.String =>
                argumentsElement.GetString(),

            JsonValueKind.Object or
            JsonValueKind.Array or
            JsonValueKind.Number or
            JsonValueKind.True or
            JsonValueKind.False or
            JsonValueKind.Null =>
                argumentsElement.GetRawText(),

            _ => null
        };
    }

    private static string? GetToolName(JsonElement root)
    {
        if (TryGetStringProperty(root, "name", out var name))
            return name;

        if (TryGetStringProperty(root, "tool", out var tool))
            return tool;

        if (TryGetStringProperty(
                root,
                "function",
                out var functionName))
        {
            return functionName;
        }

        if (TryGetPropertyIgnoreCase(
                root,
                "function",
                out var functionElement) &&
            functionElement.ValueKind == JsonValueKind.Object &&
            TryGetStringProperty(
                functionElement,
                "name",
                out var nestedFunctionName))
        {
            return nestedFunctionName;
        }

        return null;
    }

    private static bool TryGetArguments(
        JsonElement root,
        out JsonElement argumentsElement)
    {
        if (TryGetPropertyIgnoreCase(
                root,
                "arguments",
                out argumentsElement))
        {
            return true;
        }

        if (TryGetPropertyIgnoreCase(
                root,
                "parameters",
                out argumentsElement))
        {
            return true;
        }

        if (TryGetPropertyIgnoreCase(
                root,
                "function",
                out var functionElement) &&
            functionElement.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropertyIgnoreCase(
                    functionElement,
                    "arguments",
                    out argumentsElement))
            {
                return true;
            }

            if (TryGetPropertyIgnoreCase(
                    functionElement,
                    "parameters",
                    out argumentsElement))
            {
                return true;
            }
        }

        argumentsElement = default;
        return false;
    }

    private static bool TryGetStringProperty(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (!TryGetPropertyIgnoreCase(
                element,
                propertyName,
                out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
            return false;

        var text = property.GetString();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        value = text;
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        foreach (var current in element.EnumerateObject())
        {
            if (string.Equals(
                    current.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                property = current.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
