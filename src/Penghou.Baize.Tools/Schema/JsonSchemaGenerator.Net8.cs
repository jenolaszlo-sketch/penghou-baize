#if NET8_0
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Tools.Schema;

public static partial class JsonSchemaGenerator
{
    private static readonly NullabilityInfoContext Nullability = new();

    private static JsonNode GenerateSchemaNodeForNet8(Type type) =>
        GenerateTypeSchema(type, []);

    private static JsonObject GenerateTypeSchema(
        Type type,
        HashSet<Type> ancestors)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string) || type == typeof(char) ||
            type == typeof(Guid) || type == typeof(Uri) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan))
            return new JsonObject { ["type"] = "string" };

        if (type == typeof(bool))
            return new JsonObject { ["type"] = "boolean" };

        if (IsInteger(type))
            return new JsonObject { ["type"] = "integer" };

        if (IsNumber(type))
            return new JsonObject { ["type"] = "number" };

        if (type.IsEnum)
        {
            var stringEncoded = type.IsDefined(
                typeof(JsonConverterAttribute),
                inherit: false);
            return new JsonObject
            {
                ["type"] = stringEncoded ? "string" : "integer",
                ["enum"] = new JsonArray(
                    Enum.GetValues(type)
                        .Cast<object>()
                        .Select(value => stringEncoded
                            ? JsonValue.Create(value.ToString())
                            : JsonValue.Create(Convert.ToInt64(value)))
                        .ToArray<JsonNode?>())
            };
        }

        if (TryGetDictionaryValueType(type, out var valueType))
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = GenerateTypeSchema(valueType, ancestors)
            };
        }

        if (TryGetEnumerableElementType(type, out var elementType))
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = GenerateTypeSchema(elementType, ancestors)
            };
        }

        if (!ancestors.Add(type))
            return new JsonObject { ["type"] = "object" };

        try
        {
            return GenerateObjectSchema(type, ancestors);
        }
        finally
        {
            ancestors.Remove(type);
        }
    }

    private static JsonObject GenerateObjectSchema(
        Type type,
        HashSet<Type> ancestors)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var property in type.GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null ||
                property.GetIndexParameters().Length != 0 ||
                property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition ==
                JsonIgnoreCondition.Always)
                continue;

            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
                       property.Name;
            var propertySchema = GenerateTypeSchema(property.PropertyType, ancestors);
            var description = property
                .GetCustomAttribute<SchemaDescriptionAttribute>()?.Description;
            if (description is not null)
                propertySchema["description"] = description;

            properties[name] = propertySchema;

            if (IsRequired(property))
                required.Add(name);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
            schema["required"] = required;

        return schema;
    }

    private static bool IsRequired(PropertyInfo property)
    {
        if (property.IsDefined(typeof(RequiredMemberAttribute), inherit: true) ||
            property.IsDefined(typeof(JsonRequiredAttribute), inherit: true))
            return true;

        if (property.PropertyType.IsValueType)
            return Nullable.GetUnderlyingType(property.PropertyType) is null;

        return Nullability.Create(property).ReadState == NullabilityState.NotNull;
    }

    private static bool TryGetDictionaryValueType(
        Type type,
        out Type valueType)
    {
        var dictionary = FindGenericInterface(type, typeof(IDictionary<,>)) ??
                         FindGenericInterface(type, typeof(IReadOnlyDictionary<,>));
        if (dictionary is not null &&
            dictionary.GetGenericArguments()[0] == typeof(string))
        {
            valueType = dictionary.GetGenericArguments()[1];
            return true;
        }

        valueType = null!;
        return false;
    }

    private static bool TryGetEnumerableElementType(
        Type type,
        out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerable = FindGenericInterface(type, typeof(IEnumerable<>));
        if (enumerable is not null && type != typeof(string))
        {
            elementType = enumerable.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    private static Type? FindGenericInterface(Type type, Type definition)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == definition)
            return type;

        return type.GetInterfaces().FirstOrDefault(candidate =>
            candidate.IsGenericType &&
            candidate.GetGenericTypeDefinition() == definition);
    }

    private static bool IsInteger(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong);

    private static bool IsNumber(Type type) =>
        type == typeof(float) || type == typeof(double) ||
        type == typeof(decimal);
}
#endif
