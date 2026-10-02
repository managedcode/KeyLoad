using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace KeyLoad.Server;

/// <summary>Bounded format annotations and native metadata-preserving enum projection.</summary>
internal static class McpSchemaTransform
{
    internal static JsonNode Input(JsonSchemaExporterContext context, JsonNode schema) => Transform(context, schema, true);
    internal static JsonNode Output(JsonSchemaExporterContext context, JsonNode schema) => Transform(context, schema, false);

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema, bool input)
    {
        var type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;
        ValidateOpaqueShape(type, schema);
        if (type == typeof(ReadOnlyMemory<byte>) && schema is JsonObject bytes)
        {
            bytes[McpSchemaKeywords.ContentEncoding] = McpSchemaKeywords.Base64;
        }
        if (input && type == typeof(Guid) && context.PropertyInfo?.Name is McpCatalogProtocol.CommandId or McpCatalogProtocol.RequestId &&
            schema is JsonObject identity)
        {
            identity[McpSchemaKeywords.Not] = new JsonObject
            {
                [McpSchemaKeywords.Const] = Guid.Empty.ToString(McpSchemaKeywords.GuidFormat)
            };
        }
        return McpEnumSchema.Transform(context, schema, input);
    }

    private static void ValidateOpaqueShape(Type type, JsonNode schema)
    {
        if (type == typeof(JsonElement))
        { return; }
        if (schema is JsonValue value && value.TryGetValue<bool>(out var open) && open)
        {
            throw new InvalidOperationException(McpCatalogProtocol.UnsupportedSchema);
        }
        if (schema is JsonObject node && !node.ContainsKey(McpSchemaKeywords.Type) && !node.ContainsKey(McpSchemaKeywords.Reference) &&
            !node.ContainsKey(McpSchemaKeywords.Enum) && !node.ContainsKey(McpSchemaKeywords.Const) &&
            !node.ContainsKey(McpSchemaKeywords.Properties) && !node.ContainsKey(McpSchemaKeywords.Items) &&
            !node.ContainsKey(McpSchemaKeywords.AnyOf) && !node.ContainsKey(McpSchemaKeywords.OneOf) && !node.ContainsKey(McpSchemaKeywords.AllOf))
        {
            throw new InvalidOperationException(McpCatalogProtocol.UnsupportedSchema);
        }
    }
}
