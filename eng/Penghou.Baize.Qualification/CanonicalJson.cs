using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Qualification.Planning;

// canonical-json@1: ordinal object keys, decimal numbers, ordered arrays. Set
// normalization is schema-owned, never a heuristic applied to arbitrary arrays.
internal static class CanonicalJson
{
    public const string Version = "canonical-json@1";
    // Plain (non-scientific) decimal notation with trailing zeros removed. "G29"
    // is unsuitable: it renders 1e-5 magnitudes as "1E-05".
    private const string DecimalFormat = "0.#############################";
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            MaxDepth = 48
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public static T Read<T>(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
        Unique(document.RootElement);
        return document.RootElement.Deserialize<T>(Options) ?? throw new PlanningException("json-null");
    }

    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new PlanningException("json-duplicate-property");
                Unique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) Unique(value);
    }

    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, Options);
    public static byte[] Write(object value) => WriteElement(Element(value));
    public static byte[] WriteElement(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteValue(writer, value);
        return stream.ToArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(Decimal(value.GetDecimal()));
                break;
            default: value.WriteTo(writer); break;
        }
    }

    public static string Decimal(decimal value) => value.ToString(DecimalFormat, CultureInfo.InvariantCulture);
    public static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Identity(object value) => Hash(Write(value));
    public static string Money(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new PlanningException("invalid-money");
        return Decimal(amount);
    }
}
