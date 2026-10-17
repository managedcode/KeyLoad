using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PartitionQueryMcpSchemaTypes
{
    private const string MaximumReferenceDepthMessage = "The partition-query schema reference is too deep.";
    private const int MaximumReferenceDepth = 8;

    internal static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        for (var depth = 0; depth < MaximumReferenceDepth && schema.TryGetProperty(McpDiscoveryProtocol.ReferenceKeyword, out var reference); depth++)
        {
            var pointer = reference.GetString();
            if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(MaximumReferenceDepthMessage);
            }
            schema = root;
            foreach (var segment in pointer[2..].Split('/'))
            {
                schema = schema.GetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
            }
        }
        if (schema.TryGetProperty(McpDiscoveryProtocol.ReferenceKeyword, out _))
        {
            throw new InvalidOperationException(MaximumReferenceDepthMessage);
        }
        return schema;
    }

    internal static bool HasType(JsonElement schema, string expected)
    {
        if (expected == McpDiscoveryProtocol.Integer)
        {
            return HasDirectType(schema, expected, allowNullType: false);
        }
        if (HasDirectType(schema, expected, allowNullType: false))
        {
            return true;
        }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (schema.TryGetProperty(keyword, out var variants) && variants.EnumerateArray().Any(item => HasType(item, expected)))
            {
                return true;
            }
        }
        return false;
    }

    internal static JsonElement FindBranch(JsonElement schema, string expectedType, bool allowNullType = false)
    {
        if (HasDirectType(schema, expectedType, allowNullType))
        {
            return schema;
        }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (!schema.TryGetProperty(keyword, out var variants))
            {
                continue;
            }
            foreach (var variant in variants.EnumerateArray())
            {
                if (HasDirectType(variant, expectedType, allowNullType))
                {
                    return variant;
                }
            }
        }
        throw new InvalidOperationException(MaximumReferenceDepthMessage);
    }

    private static bool HasDirectType(JsonElement schema, string expected, bool allowNullType)
    {
        if (!schema.TryGetProperty(McpDiscoveryProtocol.Type, out var actual))
        {
            return false;
        }
        if (actual.ValueKind == JsonValueKind.String)
        {
            return actual.GetString() == expected;
        }
        if (actual.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var types = actual.EnumerateArray().ToArray();
        if (!allowNullType && expected == McpDiscoveryProtocol.Integer)
        {
            return types.Length == 2 && types.All(item => item.ValueKind == JsonValueKind.String)
                && types.Count(item => item.GetString() == expected) == 1
                && types.Count(item => item.GetString() == McpDiscoveryProtocol.String) == 1;
        }
        if (types.Length == 1)
        {
            return types[0].ValueKind == JsonValueKind.String && types[0].GetString() == expected;
        }
        return allowNullType && types.Length == 2
            && types.All(item => item.ValueKind == JsonValueKind.String)
            && types.Count(item => item.GetString() == expected) == 1
            && types.Count(item => item.GetString() == McpDiscoveryProtocol.Null) == 1;
    }

    internal static async Task VerifyExactTypeSetAsync(JsonElement root, JsonElement schema, string first, string second)
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
}
