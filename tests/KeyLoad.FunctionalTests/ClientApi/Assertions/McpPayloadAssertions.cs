using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Independent public JSON and actual typed execution-payload assertions for MCP DTO cases.</summary>
internal static class McpPayloadAssertions
{
    internal static async Task<object> DecodeCanonical(McpDecodeCase item)
    {
        var descriptor = Find(item.Name);
        var decoded = descriptor.Decode(item.Arguments());
        await Assert.That(decoded.Payload.Length).IsGreaterThan(0);
        return await AssertFullPublicPayload(item, decoded.Payload);
    }

    internal static async Task AssertWritersAgree(McpDecodeCase item, object decoded)
    {
        var arrayBytes = item.RuntimeSerialize(decoded);
        using var stream = new MemoryStream();
        item.SerializeToStream(decoded, stream);
        await Assert.That(stream.ToArray().AsSpan().SequenceEqual(arrayBytes)).IsTrue();
        _ = await AssertFullPublicPayload(item, arrayBytes);
    }

    internal static async Task<object> AssertFullPublicPayload(McpDecodeCase item, ReadOnlyMemory<byte> payload)
    {
        OrleansSerialization.Validate(payload.Span);
        var expected = item.DeserializePublic(item.Request);
        var actual = item.RuntimeDeserialize(payload);
        await Assert.That(actual.GetType()).IsEqualTo(expected.GetType());
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(item.Request.GetRawText()),
            JsonNode.Parse(item.SerializePublic(expected).GetRawText()))).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(item.Request.GetRawText()),
            JsonNode.Parse(item.SerializePublic(actual).GetRawText()))).IsTrue();
        return actual;
    }

    internal static async Task<T> AssertTypedPublicPayload<T>(JsonElement request, ReadOnlyMemory<byte> payload)
    {
        OrleansSerialization.Validate(payload.Span);
        var expected = request.Deserialize<T>(JsonDefaults.Options)!;
        var actual = OrleansSerialization.Deserialize<T>(payload.Span);
        await Assert.That(actual!.GetType()).IsEqualTo(expected!.GetType());
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(request.GetRawText()),
            JsonNode.Parse(JsonSerializer.Serialize(expected, JsonDefaults.Options)))).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(request.GetRawText()),
            JsonNode.Parse(JsonSerializer.Serialize(actual, JsonDefaults.Options)))).IsTrue();
        return actual;
    }

    private static McpOperationDescriptor Find(string name)
    {
        if (!McpOperationCatalog.TryGet(name, out var descriptor))
        {
            throw new InvalidOperationException(name);
        }
        return descriptor!;
    }
}
