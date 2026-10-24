using System.Text.Json;

namespace KeyLoad.IntegrationTests.ClientApi;

internal static class McpGraphPathSchemaTypes
{
    private const string ReferenceKeyword = "$ref";
    private const int MaximumReferenceDepth = 8;
    private const string InvalidSchemaType = "The accepted path schema type is missing.";

    internal static bool ContainsType(JsonElement schema, string expected, JsonElement root)
    {
        schema = Resolve(root, schema);
        if (schema.TryGetProperty(McpDiscoveryProtocol.Type, out var type)
            && ((type.ValueKind == JsonValueKind.String && type.GetString() == expected)
                || (type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.GetString() == expected))))
        {
            return true;
        }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (schema.TryGetProperty(keyword, out var variants) && variants.ValueKind == JsonValueKind.Array
                && variants.EnumerateArray().Any(variant => ContainsType(variant, expected, root)))
            {
                return true;
            }
        }
        return false;
    }

    internal static JsonElement FindType(JsonElement schema, string expected, JsonElement root)
    {
        schema = Resolve(root, schema);
        if (schema.TryGetProperty(McpDiscoveryProtocol.Type, out var type)
            && ((type.ValueKind == JsonValueKind.String && type.GetString() == expected)
                || (type.ValueKind == JsonValueKind.Array && type.EnumerateArray()
                    .Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == expected))))
        {
            return schema;
        }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (!schema.TryGetProperty(keyword, out var variants) || variants.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var variant in variants.EnumerateArray())
            {
                if (ContainsType(variant, expected, root))
                {
                    return FindType(variant, expected, root);
                }
            }
        }
        throw new InvalidOperationException(InvalidSchemaType);
    }

    internal static async Task VerifyExactTypeSetAsync(JsonElement schema, JsonElement root, string first, string second)
    {
        schema = Resolve(root, schema);
        var type = schema.GetProperty(McpDiscoveryProtocol.Type);
        await Assert.That(type.ValueKind).IsEqualTo(JsonValueKind.Array);
        var types = type.EnumerateArray().ToArray();
        await Assert.That(types.Length).IsEqualTo(2);
        await Assert.That(types.All(item => item.ValueKind == JsonValueKind.String)).IsTrue();
        await Assert.That(types.Count(item => item.GetString() == first)).IsEqualTo(1);
        await Assert.That(types.Count(item => item.GetString() == second)).IsEqualTo(1);
    }

    internal static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        for (var depth = 0; depth < MaximumReferenceDepth && schema.TryGetProperty(ReferenceKeyword, out var reference); depth++)
        {
            var pointer = reference.GetString();
            if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The path schema reference is not local.");
            }
            schema = root;
            foreach (var segment in pointer[2..].Split('/'))
            {
                schema = schema.GetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
            }
        }
        if (schema.TryGetProperty(ReferenceKeyword, out _))
        {
            throw new InvalidOperationException("The path schema reference is too deep.");
        }
        return schema;
    }
}
