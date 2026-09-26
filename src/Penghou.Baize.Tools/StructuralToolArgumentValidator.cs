using System.Text.Json;
using System.Text.RegularExpressions;

namespace Penghou.Baize.Tools;

/// <summary>
/// Default <see cref="ILlmToolArgumentValidator"/> implementation enforcing the
/// structural JSON Schema subset: object shape, required properties, nested
/// properties, enums, constants, common scalar constraints (length, pattern,
/// ranges, cardinality), and <c>additionalProperties</c>. Anything outside
/// this subset is reported in
/// <see cref="LlmToolArgumentValidationResult.UnsupportedKeywords"/>
/// explicitly rather than silently ignored; applications needing full dialect
/// coverage replace this validator through dependency injection.
/// </summary>
public sealed class StructuralToolArgumentValidator : ILlmToolArgumentValidator
{
    /// <summary>Validator identity recorded in every outcome.</summary>
    public const string ValidatorId = "structural/v1";

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly HashSet<string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties",
        "items", "enum", "const",
        "minLength", "maxLength", "pattern",
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum",
        "minItems", "maxItems", "minProperties", "maxProperties",
        "description", "title", "default", "examples",
    };

    /// <inheritdoc />
    public Task<LlmToolArgumentValidationResult> ValidateAsync(
        LlmTool tool,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(argumentsJson);
        var failures = new List<LlmToolArgumentValidationFailure>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);

        JsonDocument schemaDocument;
        try
        {
            schemaDocument = JsonDocument.Parse(tool.InputSchemaJson);
        }
        catch (JsonException)
        {
            return Task.FromResult(Invalid(
                [new LlmToolArgumentValidationFailure(
                    "$", "schema-error", "The tool input schema is not valid JSON.")]));
        }

        using (schemaDocument)
        {
            JsonDocument argumentsDocument;
            try
            {
                argumentsDocument = JsonDocument.Parse(argumentsJson);
            }
            catch (JsonException)
            {
                return Task.FromResult(Invalid(
                    [new LlmToolArgumentValidationFailure(
                        "$", "invalid-json", "The tool arguments are not valid JSON.")]));
            }

            using (argumentsDocument)
            {
                ValidateNode(
                    argumentsDocument.RootElement,
                    schemaDocument.RootElement,
                    "$",
                    failures,
                    unsupported,
                    cancellationToken);
            }
        }

        return Task.FromResult(
            failures.Count == 0
                ? new LlmToolArgumentValidationResult(
                    true, [], ValidatorId, unsupported.OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray())
                : new LlmToolArgumentValidationResult(
                    false, failures, ValidatorId, unsupported.OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray()));
    }

    private static LlmToolArgumentValidationResult Invalid(
        IReadOnlyList<LlmToolArgumentValidationFailure> failures) =>
        new(false, failures, ValidatorId, []);

    private static void ValidateNode(
        JsonElement value,
        JsonElement schema,
        string path,
        List<LlmToolArgumentValidationFailure> failures,
        HashSet<string> unsupported,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in schema.EnumerateObject())
        {
            if (!SupportedKeywords.Contains(property.Name))
            {
                unsupported.Add($"{path}.{property.Name}");
            }
        }

        if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array)
        {
            var match = allowed.EnumerateArray().Any(candidate => JsonEquals(candidate, value));
            if (!match)
            {
                failures.Add(new(path, "not-in-enum", "The value is not one of the allowed enum values."));
            }
        }

        if (schema.TryGetProperty("const", out var constant) && !JsonEquals(constant, value))
        {
            failures.Add(new(path, "const-mismatch", "The value does not equal the required constant."));
        }

        if (!schema.TryGetProperty("type", out var typeNode))
        {
            ValidateObjectMembers(value, schema, path, failures, unsupported, cancellationToken);
            return;
        }

        var types = typeNode.ValueKind == JsonValueKind.Array
            ? typeNode.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToArray()
            : typeNode.ValueKind == JsonValueKind.String
                ? [typeNode.GetString()!]
                : [];
        if (types.Length == 0)
        {
            failures.Add(new(path, "schema-error", "The schema type is not a valid type name."));
            return;
        }

        if (!types.Any(type => MatchesType(value, type)))
        {
            failures.Add(new(
                path, "type-mismatch",
                $"The value does not match the expected type '{string.Join("/", types)}'."));
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                ValidateString(value.GetString()!, schema, path, failures);
                break;
            case JsonValueKind.Number:
                ValidateNumber(value, schema, path, failures);
                break;
            case JsonValueKind.Array:
                ValidateArray(value, schema, path, failures, unsupported, cancellationToken);
                break;
            case JsonValueKind.Object:
                ValidateObjectMembers(value, schema, path, failures, unsupported, cancellationToken);
                break;
            default:
                break;
        }
    }

    private static void ValidateObjectMembers(
        JsonElement value,
        JsonElement schema,
        string path,
        List<LlmToolArgumentValidationFailure> failures,
        HashSet<string> unsupported,
        CancellationToken cancellationToken)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var requiredNode) &&
            requiredNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredNode.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } name)
                {
                    required.Add(name);
                }
            }
        }

        var properties = schema.TryGetProperty("properties", out var propertiesNode) &&
            propertiesNode.ValueKind == JsonValueKind.Object
            ? propertiesNode
            : (JsonElement?)null;
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (properties.HasValue)
        {
            foreach (var property in properties.Value.EnumerateObject())
            {
                known.Add(property.Name);
            }
        }

        foreach (var name in required)
        {
            if (!value.TryGetProperty(name, out _))
            {
                failures.Add(new($"{path}.{name}", "required-missing", $"Required property '{name}' is missing."));
            }
        }

        var allowAdditional = true;
        if (schema.TryGetProperty("additionalProperties", out var additional))
        {
            if (additional.ValueKind is JsonValueKind.False)
            {
                allowAdditional = false;
            }
            else if (additional.ValueKind == JsonValueKind.Object)
            {
                unsupported.Add($"{path}.additionalProperties");
            }
        }

        foreach (var property in value.EnumerateObject())
        {
            var childPath = $"{path}.{property.Name}";
            if (properties.HasValue &&
                properties.Value.TryGetProperty(property.Name, out var childSchema))
            {
                ValidateNode(property.Value, childSchema, childPath, failures, unsupported, cancellationToken);
            }
            else if (!known.Contains(property.Name) && !allowAdditional)
            {
                failures.Add(new(childPath, "unknown-property", $"Property '{property.Name}' is not declared."));
            }
        }

        if (schema.TryGetProperty("minProperties", out var minProperties) &&
            TryGetInt(minProperties, out var minCount) &&
            CountProperties(value) < minCount)
        {
            failures.Add(new(path, "too-few-properties", "The object has fewer properties than required."));
        }

        if (schema.TryGetProperty("maxProperties", out var maxProperties) &&
            TryGetInt(maxProperties, out var maxCount) &&
            CountProperties(value) > maxCount)
        {
            failures.Add(new(path, "too-many-properties", "The object has more properties than allowed."));
        }
    }

    private static bool TryGetInt(JsonElement node, out int value)
    {
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static void ValidateString(
        string value,
        JsonElement schema,
        string path,
        List<LlmToolArgumentValidationFailure> failures)
    {
        if (schema.TryGetProperty("minLength", out var minLength) &&
            TryGetInt(minLength, out var minLengthValue) &&
            value.Length < minLengthValue)
        {
            failures.Add(new(path, "too-short", "The string is shorter than the minimum length."));
        }

        if (schema.TryGetProperty("maxLength", out var maxLength) &&
            TryGetInt(maxLength, out var maxLengthValue) &&
            value.Length > maxLengthValue)
        {
            failures.Add(new(path, "too-long", "The string is longer than the maximum length."));
        }

        if (schema.TryGetProperty("pattern", out var pattern) &&
            pattern.ValueKind == JsonValueKind.String &&
            pattern.GetString() is { } expression)
        {
            Regex regex;
            try
            {
                regex = new Regex(expression, RegexOptions.None, PatternTimeout);
            }
            catch (ArgumentException)
            {
                failures.Add(new(path, "schema-error", "The pattern is not a valid regular expression."));
                return;
            }

            bool matches;
            try
            {
                matches = regex.IsMatch(value);
            }
            catch (RegexMatchTimeoutException)
            {
                failures.Add(new(path, "pattern-timeout", "Pattern matching exceeded the time budget."));
                return;
            }

            if (!matches)
            {
                failures.Add(new(path, "pattern-mismatch", "The string does not match the required pattern."));
            }
        }
    }

    private static void ValidateNumber(
        JsonElement value,
        JsonElement schema,
        string path,
        List<LlmToolArgumentValidationFailure> failures)
    {
        if (!TryGetDouble(value, out var number))
        {
            return;
        }

        if (schema.TryGetProperty("minimum", out var minimum) &&
            minimum.ValueKind == JsonValueKind.Number &&
            number < minimum.GetDouble())
        {
            failures.Add(new(path, "out-of-range", "The number is below the minimum."));
        }

        if (schema.TryGetProperty("exclusiveMinimum", out var exclusiveMinimum) &&
            exclusiveMinimum.ValueKind == JsonValueKind.Number &&
            number <= exclusiveMinimum.GetDouble())
        {
            failures.Add(new(path, "out-of-range", "The number is not above the exclusive minimum."));
        }

        if (schema.TryGetProperty("maximum", out var maximum) &&
            maximum.ValueKind == JsonValueKind.Number &&
            number > maximum.GetDouble())
        {
            failures.Add(new(path, "out-of-range", "The number is above the maximum."));
        }

        if (schema.TryGetProperty("exclusiveMaximum", out var exclusiveMaximum) &&
            exclusiveMaximum.ValueKind == JsonValueKind.Number &&
            number >= exclusiveMaximum.GetDouble())
        {
            failures.Add(new(path, "out-of-range", "The number is not below the exclusive maximum."));
        }
    }

    private static void ValidateArray(
        JsonElement value,
        JsonElement schema,
        string path,
        List<LlmToolArgumentValidationFailure> failures,
        HashSet<string> unsupported,
        CancellationToken cancellationToken)
    {
        var count = value.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var minItems) &&
            TryGetInt(minItems, out var minItemsValue) &&
            count < minItemsValue)
        {
            failures.Add(new(path, "too-few-items", "The array has fewer items than required."));
        }

        if (schema.TryGetProperty("maxItems", out var maxItems) &&
            TryGetInt(maxItems, out var maxItemsValue) &&
            count > maxItemsValue)
        {
            failures.Add(new(path, "too-many-items", "The array has more items than allowed."));
        }

        if (schema.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateNode(item, items, $"{path}[{index}]", failures, unsupported, cancellationToken);
                index++;
            }
        }
        else if (schema.TryGetProperty("items", out var tupleItems) &&
            tupleItems.ValueKind == JsonValueKind.Array)
        {
            unsupported.Add($"{path}.items[]");
        }
    }

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        _ => false,
    };

    private static bool TryGetDouble(JsonElement value, out double number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number))
        {
            return true;
        }

        number = 0;
        return false;
    }

    private static int CountProperties(JsonElement value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateObject())
        {
            count++;
        }

        return count;
    }

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number &&
                TryGetDouble(left, out var leftNumber) && TryGetDouble(right, out var rightNumber))
            {
                return leftNumber == rightNumber;
            }

            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectEquals(left, right),
            JsonValueKind.Array => ArrayEquals(left, right),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText() ||
                (TryGetDouble(left, out var leftNumber) &&
                 TryGetDouble(right, out var rightNumber) &&
                 leftNumber == rightNumber),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            _ => true,
        };
    }

    private static bool ObjectEquals(JsonElement left, JsonElement right)
    {
        var leftProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in left.EnumerateObject())
        {
            leftProperties[property.Name] = property.Value;
        }

        foreach (var property in right.EnumerateObject())
        {
            if (!leftProperties.TryGetValue(property.Name, out var other) ||
                !JsonEquals(other, property.Value))
            {
                return false;
            }

            leftProperties.Remove(property.Name);
        }

        return leftProperties.Count == 0;
    }

    private static bool ArrayEquals(JsonElement left, JsonElement right)
    {
        var leftItems = new List<JsonElement>();
        var rightItems = new List<JsonElement>();
        foreach (var item in left.EnumerateArray())
        {
            leftItems.Add(item);
        }

        foreach (var item in right.EnumerateArray())
        {
            rightItems.Add(item);
        }

        return leftItems.Count == rightItems.Count &&
            leftItems.Zip(rightItems).All(pair => JsonEquals(pair.First, pair.Second));
    }
}
