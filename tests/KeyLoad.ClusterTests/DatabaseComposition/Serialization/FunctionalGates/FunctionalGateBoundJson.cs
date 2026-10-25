using System.Text;
using System.Text.Json;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Orders trusted literal expected JSON without borrowing production canonicalization or actual results.</summary>
internal static class FunctionalGateBoundJson
{
    internal static string Serialize(object value)
    {
        var bytes = JsonDefaults.Serialize(value);
        using var document = JsonDocument.Parse(bytes);
        using var output = new MemoryStream(bytes.Length);
        using (var writer = new Utf8JsonWriter(output))
        {
            Write(writer, document.RootElement);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var element in value.EnumerateArray())
            {
                Write(writer, element);
            }
            writer.WriteEndArray();
        }
        else
        {
            value.WriteTo(writer);
        }
    }
}
