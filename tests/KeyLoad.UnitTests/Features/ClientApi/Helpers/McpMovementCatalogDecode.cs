using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Strict public rejection followed by complete healthy native decoding of the independent original movement request.</summary>
internal static class McpMovementCatalogDecode
{
    private const string OriginalId = "626ac37f-a3ab-421f-8b9b-660b8d0f9580";
    private const string TargetId = "93db94e0-1fb6-4a2b-bc95-8e5d1d5410b5";
    private const string Tenant = "catalog-tenant";
    private const string Database = "catalog-database";
    private const string Domain = "catalog-domain";
    private const string PartitionKey = "catalog-partition";
    private const long OriginalRevision = 7;

    internal static async Task RequireAsync(McpOperationDescriptor descriptor)
    {
        var expected = new PartitionMoveRequest(Guid.Parse(OriginalId), new PartitionRef(Tenant, Database, Domain, PartitionKey),
            Guid.Parse(TargetId), OriginalRevision, PartitionMoveMode.Transfer);
        var original = JsonDefaults.Serialize(expected);
        await DeniedAsync(descriptor, new Dictionary<string, JsonElement>(StringComparer.Ordinal), expected);
        await DeniedAsync(descriptor, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [McpMovementCatalogProtocol.Request] = JsonSerializer.SerializeToElement<object?>(null) }, expected);
        var unknown = Arguments(original);
        unknown[McpCanonicalTestData.UnknownKey] = JsonSerializer.SerializeToElement(Tenant);
        await DeniedAsync(descriptor, unknown, expected);
        var emptyId = JsonNode.Parse(original)!.AsObject();
        emptyId[McpMovementCatalogProtocol.MoveId] = JsonValue.Create(Guid.Empty);
        await DeniedAsync(descriptor, Arguments(JsonSerializer.SerializeToUtf8Bytes(emptyId)), expected);
        var extra = JsonNode.Parse(original)!.AsObject();
        extra[McpCanonicalTestData.UnknownKey] = JsonValue.Create(Tenant);
        await DeniedAsync(descriptor, Arguments(JsonSerializer.SerializeToUtf8Bytes(extra)), expected);
        await ComputedMetadataAsync(descriptor, original, expected);
        McpDecodedOperation decoded;
        using (var document = JsonDocument.Parse(original))
        {
            decoded = descriptor.Decode(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            { [McpMovementCatalogProtocol.Request] = document.RootElement });
        }
        await HealthyAsync(decoded, expected);
    }

    private static async Task ComputedMetadataAsync(McpOperationDescriptor descriptor, byte[] original, PartitionMoveRequest expected)
    {
        var omitted = JsonNode.Parse(original)!.AsObject();
        omitted[McpMovementCatalogProtocol.Partition]!.AsObject().Remove(McpMovementCatalogProtocol.Atomic);
        await HealthyAsync(descriptor.Decode(Arguments(JsonSerializer.SerializeToUtf8Bytes(omitted))), expected);
        JsonNode?[] metadata = [null, JsonValue.Create(Tenant)];
        foreach (var value in metadata)
        {
            var supplied = JsonNode.Parse(original)!.AsObject();
            supplied[McpMovementCatalogProtocol.Partition]!.AsObject()[McpMovementCatalogProtocol.Atomic] = value;
            await HealthyAsync(descriptor.Decode(Arguments(JsonSerializer.SerializeToUtf8Bytes(supplied))), expected);
        }
    }

    private static async Task DeniedAsync(McpOperationDescriptor descriptor, Dictionary<string, JsonElement> arguments, PartitionMoveRequest expected)
    {
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(arguments)).Code).IsEqualTo(ErrorCode.Validation);
        await HealthyAsync(descriptor.Decode(Arguments(JsonDefaults.Serialize(expected))), expected);
    }

    private static async Task HealthyAsync(McpDecodedOperation decoded, PartitionMoveRequest expected)
    {
        await Assert.That(decoded.ReadKind).IsNull();
        await Assert.That(decoded.CommandKind).IsEqualTo(OperationKind.MovePartition);
        await Assert.That(decoded.CommandId).IsEqualTo(expected.MoveId);
        var actual = NativeSerialization.Deserialize<PartitionMoveRequest>(decoded.Payload.Span);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static Dictionary<string, JsonElement> Arguments(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [McpMovementCatalogProtocol.Request] = document.RootElement.Clone() };
    }
}
