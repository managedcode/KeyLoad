using System.Text;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Orders trusted literal expected JSON without borrowing production canonicalization or actual results.</summary>
internal static class FunctionalGateBoundJson
{
    internal static string Serialize(object value)
    {
        var limits = IntegrationExecutionOptions.DatabaseLimits().Value;
        var bytes = JsonDefaults.Serialize(value);
        if (bytes.Length > limits.MaxDocumentBytes)
        {
            throw new InvalidOperationException("The trusted functional-gate JSON exceeds the original document bound.");
        }
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
        using var output = new MemoryStream(bytes.Length);
        using (var writer = new Utf8JsonWriter(output))
        {
            Write(writer, document.RootElement);
        }
        if (output.Length > limits.MaxDocumentBytes)
        {
            throw new InvalidOperationException("The trusted functional-gate JSON exceeds the original document bound.");
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
