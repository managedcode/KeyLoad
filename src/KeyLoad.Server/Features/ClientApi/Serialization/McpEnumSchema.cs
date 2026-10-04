using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace KeyLoad.Server;

/// <summary>Projects existing native string-enum metadata without narrowing canonical numeric or flags handling.</summary>
internal static class McpEnumSchema
{
    private static readonly string[] Annotations =
        [McpSchemaKeywords.Default, McpSchemaKeywords.Title, McpSchemaKeywords.Description, McpSchemaKeywords.Examples];

    internal static JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema, bool input)
    {
        var underlying = Nullable.GetUnderlyingType(context.TypeInfo.Type);
        var type = underlying ?? context.TypeInfo.Type;
        if (!type.IsEnum || schema is not JsonObject native)
        { return schema; }
        var outer = new JsonObject();
        MoveAnnotations(native, outer);
        var strings = input ? InputStrings(native) : native;
        var branches = new JsonArray(strings, Integer(Enum.GetUnderlyingType(type)));
        if (underlying is not null)
        { branches.Add(new JsonObject { [McpSchemaKeywords.Type] = McpSchemaKeywords.Null }); }
        outer[McpSchemaKeywords.AnyOf] = branches;
        return outer;
    }

    private static JsonObject InputStrings(JsonObject native)
    {
        var strings = new JsonObject { [McpSchemaKeywords.Type] = McpSchemaKeywords.String };
        if (native[McpSchemaKeywords.Enum] is JsonArray names)
        {
            strings[McpSchemaKeywords.Examples] = new JsonArray(names.Where(name => name is not null)
                .Select(name => name!.DeepClone()).ToArray());
        }
        return strings;
    }

    private static void MoveAnnotations(JsonObject native, JsonObject outer)
    {
        foreach (var key in Annotations)
        {
            if (!native.TryGetPropertyValue(key, out var value))
            { continue; }
            native.Remove(key);
            outer[key] = value;
        }
    }

    private static JsonObject Integer(Type underlying)
    {
        var bounds = Bounds(underlying);
        return new JsonObject
        {
            [McpSchemaKeywords.Type] = McpSchemaKeywords.Integer,
            [McpSchemaKeywords.Minimum] = bounds.Minimum,
            [McpSchemaKeywords.Maximum] = bounds.Maximum
        };
    }

    private static (JsonNode Minimum, JsonNode Maximum) Bounds(Type underlying) =>
        Type.GetTypeCode(underlying) switch
        {
            TypeCode.SByte => (JsonValue.Create(sbyte.MinValue)!, JsonValue.Create(sbyte.MaxValue)!),
            TypeCode.Byte => (JsonValue.Create(byte.MinValue)!, JsonValue.Create(byte.MaxValue)!),
            TypeCode.Int16 => (JsonValue.Create(short.MinValue)!, JsonValue.Create(short.MaxValue)!),
            TypeCode.UInt16 => (JsonValue.Create(ushort.MinValue)!, JsonValue.Create(ushort.MaxValue)!),
            TypeCode.Int32 => (JsonValue.Create(int.MinValue)!, JsonValue.Create(int.MaxValue)!),
            TypeCode.UInt32 => (JsonValue.Create(uint.MinValue)!, JsonValue.Create(uint.MaxValue)!),
            TypeCode.Int64 => (JsonValue.Create(long.MinValue)!, JsonValue.Create(long.MaxValue)!),
            TypeCode.UInt64 => (JsonValue.Create(ulong.MinValue)!, JsonValue.Create(ulong.MaxValue)!),
            _ => throw new InvalidOperationException(McpCatalogProtocol.ConverterConfiguration)
        };
}
