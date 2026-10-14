using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventFeedTailCatalogAssertions
{
    private const string Name = "keyload_tail_event_feed";
    private const string Route = "/v1/event-feeds/tail";
    private const string Request = "request";
    private const string Properties = "properties";
    private const string AdditionalProperties = "additionalProperties";
    private const string Roles = "roles";
    private const int Version = 1;
    private const int Limit = 1;
    private const long Revision = 1;
    private const string Partition = "partition-offer";
    private const string Cursor = "original-signed-opaque-cursor";

    internal static async Task RequireAsync()
    {
        await Assert.That(McpOperationCatalog.TryGet(Name, out var descriptor)).IsTrue();
        var operation = descriptor!;
        await Assert.That(operation.Route).IsEqualTo(Route);
        await Assert.That(operation.CommandKind).IsEqualTo(OperationKind.EventFeedControl);
        await Assert.That(operation.ReadKind).IsNull();
        await Assert.That(operation.ReadOnly && operation.Idempotent).IsTrue();
        await Assert.That(operation.Destructive).IsFalse();
        var schema = McpSchemaInspector.DefinedRequest(operation.InputSchema);
        await Assert.That(schema.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
        await Assert.That(schema.GetProperty(Properties).EnumerateObject().Select(value => value.Name).ToArray())
            .IsEquivalentTo(new[] { "version", "commandId", "controlPartition", "mapId", "action", "scope", "start",
                "expectedRevision", "expectedCoverageGeneration", "cursor", "offerDigest", "limit", "originalFirstIssuancePublicCommandId" });
        var original = new EventFeedControlRequest
        {
            Version = Version,
            CommandId = Guid.NewGuid(),
            ControlPartition = new("tenant-offer", "database-offer", "domain-offer", Partition),
            MapId = Guid.NewGuid(),
            Action = EventFeedControlAction.Tail,
            Scope = new() { Domain = "domain-offer", Resource = "topic-offer", Kind = EventSourceKind.Topic },
            Start = EventFeedStartPolicy.FromBeginning,
            ExpectedRevision = Revision,
            ExpectedCoverageGeneration = Revision,
            Cursor = Cursor,
            Limit = Limit
        };
        var publicRequest = JsonSerializer.SerializeToElement(original, JsonDefaults.Options);
        var arguments = new Dictionary<string, JsonElement> { [Request] = publicRequest };
        var decoded = operation.Decode(arguments);
        await Assert.That(decoded.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(decoded.CommandKind).IsEqualTo(OperationKind.EventFeedControl);
        var actual = await McpNativePayloadAssertions.AssertTypedPublicPayload<EventFeedControlRequest>(publicRequest, decoded.Payload);
        var roundtrip = NativeSerialization.Deserialize<EventFeedControlRequest>(NativeSerialization.Serialize(actual));
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(publicRequest.GetRawText()),
            JsonNode.Parse(JsonSerializer.Serialize(roundtrip, JsonDefaults.Options)))).IsTrue();
        await DenialsAsync(operation, original).ConfigureAwait(false);
        _ = await McpNativePayloadAssertions.AssertTypedPublicPayload<EventFeedControlRequest>(publicRequest,
            operation.Decode(arguments).Payload);
    }

    private static async Task DenialsAsync(McpOperationDescriptor descriptor, EventFeedControlRequest original)
    {
        var wrongAction = new Dictionary<string, JsonElement>
        {
            [Request] = JsonSerializer.SerializeToElement(original with { Action = EventFeedControlAction.Open }, JsonDefaults.Options)
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(wrongAction)).Code)
            .IsEqualTo(ErrorCode.Validation);
        var authority = JsonNode.Parse(JsonSerializer.Serialize(original, JsonDefaults.Options))!.AsObject();
        authority[Roles] = new JsonArray("administrator");
        var bad = new Dictionary<string, JsonElement> { [Request] = JsonSerializer.SerializeToElement(authority) };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(bad)).Code).IsEqualTo(ErrorCode.Validation);
        bad = new()
        {
            [Request] = JsonSerializer.SerializeToElement(original, JsonDefaults.Options),
            [Roles] = JsonSerializer.SerializeToElement("administrator")
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(bad)).Code).IsEqualTo(ErrorCode.Validation);
    }
}
