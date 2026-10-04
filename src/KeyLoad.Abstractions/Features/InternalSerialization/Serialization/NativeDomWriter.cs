using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

internal static class NativeDomWriter
{
    internal static JsonElement Materialize(JsonTreeNode root)
    {
        using var buffer = new NativeDomBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = NativeSerializationLimits.SemanticDepth }))
        {
            WriteNode(writer, root);
        }
        return JsonElement.Parse(buffer.WrittenSpan);
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonTreeNode node)
    {
        switch (node.Kind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, node.Properties!);
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in node.Items!)
                {
                    WriteNode(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(JsonEncodedText.Encode(node.Text!));
                break;
            case JsonValueKind.Number:
                // Preflight already used the official parser and charged this exact lexeme.
                writer.WriteRawValue(node.Text!, skipInputValidation: true);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(node.Kind == JsonValueKind.True);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonTreeProperty[] properties)
    {
        writer.WriteStartObject();
        foreach (var property in properties)
        {
            writer.WritePropertyName(JsonEncodedText.Encode(property.Name));
            WriteNode(writer, property.Value);
        }
        writer.WriteEndObject();
    }
}
