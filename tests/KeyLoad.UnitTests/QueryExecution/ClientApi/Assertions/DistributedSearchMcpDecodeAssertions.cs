using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchMcpDecodeAssertions
{
    internal static async Task RequireAsync(McpOperationDescriptor descriptor)
    {
        var expected = DistributedSearchMcpPayloadAssertions.Request();
        await DeniedAsync(descriptor, [], expected);
        var authority = Arguments(JsonDefaults.Serialize(expected));
        authority[McpCanonicalTestData.UnknownKey] = JsonSerializer.SerializeToElement(McpCanonicalTestData.Principal);
        await DeniedAsync(descriptor, authority, expected);
        var requestAuthority = JsonNode.Parse(JsonDefaults.Serialize(expected))!.AsObject();
        requestAuthority[McpCanonicalTestData.UnknownKey] = JsonValue.Create(McpCanonicalTestData.Principal);
        await DeniedAsync(descriptor, Arguments(JsonSerializer.SerializeToUtf8Bytes(requestAuthority)), expected);
        var nestedAuthority = JsonNode.Parse(JsonDefaults.Serialize(expected))!.AsObject();
        nestedAuthority[DistributedSearchMcpProtocol.Search]!.AsObject()[McpCanonicalTestData.UnknownKey]
            = JsonValue.Create(McpCanonicalTestData.Principal);
        await DeniedAsync(descriptor, Arguments(JsonSerializer.SerializeToUtf8Bytes(nestedAuthority)), expected);
        var absentRequest = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement<object?>(null)
        };
        await DeniedAsync(descriptor, absentRequest, expected);
        McpDecodedOperation decoded;
        using (var original = JsonDocument.Parse(JsonDefaults.Serialize(expected)))
        {
            decoded = descriptor.Decode(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [McpCanonicalTestData.RequestKey] = original.RootElement
            });
        }
        await HealthyAsync(decoded, expected);
    }

    private static async Task DeniedAsync(McpOperationDescriptor descriptor,
        Dictionary<string, JsonElement> arguments, DistributedSearchRequest expected)
    {
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(arguments)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await HealthyAsync(descriptor.Decode(Arguments(JsonDefaults.Serialize(expected))), expected);
    }

    private static async Task HealthyAsync(McpDecodedOperation decoded, DistributedSearchRequest expected)
    {
        await Assert.That(decoded.ReadKind).IsEqualTo(GrainReadKind.DistributedSearch);
        await Assert.That(decoded.CommandKind).IsNull();
        await Assert.That(decoded.CommandId).IsEqualTo(Guid.Empty);
        var actual = OrleansSerialization.Deserialize<DistributedSearchRequest>(decoded.Payload.Span);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var json = JsonSerializer.Deserialize<DistributedSearchRequest>(JsonDefaults.Serialize(actual), JsonDefaults.Options);
        var roundtrip = OrleansSerialization.Deserialize<DistributedSearchRequest>(OrleansSerialization.Serialize(json));
        await Assert.That(JsonDefaults.Serialize(roundtrip).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static Dictionary<string, JsonElement> Arguments(byte[] bytes)
    {
        using var body = JsonDocument.Parse(bytes);
        return new(StringComparer.Ordinal)
        {
            [McpCanonicalTestData.RequestKey] = body.RootElement.Clone()
        };
    }
}
