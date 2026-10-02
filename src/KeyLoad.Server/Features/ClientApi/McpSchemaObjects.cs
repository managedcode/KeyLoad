using System.Text.Json.Nodes;

namespace KeyLoad.Server;

/// <summary>Small closed wrapper schema shapes shared by input and structured output.</summary>
internal static class McpSchemaObjects
{
    internal static JsonObject Closed(JsonObject properties, JsonArray required) => new()
    {
        [McpSchemaKeywords.Type] = McpSchemaKeywords.Object,
        [McpSchemaKeywords.Properties] = properties,
        [McpSchemaKeywords.Required] = required,
        [McpSchemaKeywords.AdditionalProperties] = false
    };

    internal static JsonObject Reference(string pointer) => new() { [McpSchemaKeywords.Reference] = pointer };

    internal static JsonObject Identity(bool nullable) => new()
    {
        [McpSchemaKeywords.Type] = nullable
            ? new JsonArray(McpSchemaKeywords.String, McpSchemaKeywords.Null) : JsonValue.Create(McpSchemaKeywords.String),
        [McpSchemaKeywords.Format] = McpSchemaKeywords.Uuid,
        [McpSchemaKeywords.Not] = new JsonObject { [McpSchemaKeywords.Const] = Guid.Empty.ToString(McpSchemaKeywords.GuidFormat) }
    };

    internal static JsonObject NullableResult() => new()
    {
        [McpSchemaKeywords.AnyOf] = new JsonArray(
            Reference(McpSchemaKeywords.ResultReference),
            new JsonObject { [McpSchemaKeywords.Type] = McpSchemaKeywords.Null })
    };
}
