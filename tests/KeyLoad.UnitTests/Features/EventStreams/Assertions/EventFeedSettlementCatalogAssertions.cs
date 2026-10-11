using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventFeedSettlementCatalogAssertions
{
    private const int Version = 1;
    private const long Revision = 1;
    private const int Limit = 1;
    private const string Request = "request";
    private const string Partition = "partition-settlement";
    private const string Roles = "roles";
    private const string Properties = "properties";
    private const string AdditionalProperties = "additionalProperties";
    private const string AcknowledgeName = "keyload_acknowledge_event_feed";
    private const string AcknowledgeRoute = "/v1/event-feeds/acknowledge";
    private const string ReleaseName = "keyload_release_event_feed";
    private const string ReleaseRoute = "/v1/event-feeds/release";
    private const string RefreshName = "keyload_refresh_event_feed";
    private const string RefreshRoute = "/v1/event-feeds/refresh";
    private const string OriginalCursor = "original-signed-cursor";
    private const string OriginalOffer = "original-signed-offer";

    internal static async Task RequireAsync()
    {
        await RequireOperationAsync(AcknowledgeName, AcknowledgeRoute, EventFeedControlAction.Acknowledge);
        await RequireOperationAsync(ReleaseName, ReleaseRoute, EventFeedControlAction.Release);
        await RequireOperationAsync(RefreshName, RefreshRoute, EventFeedControlAction.Refresh);
    }

    private static async Task RequireOperationAsync(string name, string route, EventFeedControlAction action)
    {
        await Assert.That(McpOperationCatalog.TryGet(name, out var descriptor)).IsTrue();
        var operation = descriptor!;
        await Assert.That(operation.Route).IsEqualTo(route);
        await Assert.That(operation.CommandKind).IsEqualTo(OperationKind.EventFeedControl);
        await Assert.That(operation.ReadKind).IsNull();
        await Assert.That(operation.ReadOnly || operation.Idempotent).IsFalse();
        await Assert.That(operation.Destructive).IsTrue();
        var schema = McpSchemaInspector.DefinedRequest(operation.InputSchema);
        await Assert.That(schema.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
        await Assert.That(schema.GetProperty(Properties).EnumerateObject().Select(value => value.Name).ToArray())
            .IsEquivalentTo(new[] { "version", "commandId", "controlPartition", "mapId", "action", "scope", "start",
                "expectedRevision", "expectedCoverageGeneration", "cursor", "offerDigest", "limit", "originalFirstIssuancePublicCommandId" });
        var original = new EventFeedControlRequest
        {
            Version = Version,
            CommandId = Guid.NewGuid(),
            ControlPartition = new("tenant-settlement", "database-settlement", "domain-settlement", Partition),
            MapId = Guid.NewGuid(),
            Action = action,
            Scope = new() { Domain = "domain-settlement", Resource = "topic-settlement", Kind = EventSourceKind.Topic },
            Start = EventFeedStartPolicy.FromBeginning,
            ExpectedRevision = Revision,
            ExpectedCoverageGeneration = Revision,
            Cursor = OriginalCursor,
            OfferDigest = action == EventFeedControlAction.Acknowledge ? OriginalOffer : null,
            Limit = Limit
        };
        await HealthyAsync(operation, original);
        await DenialsAsync(operation, original);
        await HealthyAsync(operation, original);
    }

    private static async Task HealthyAsync(McpOperationDescriptor operation, EventFeedControlRequest original)
    {
        var publicRequest = JsonSerializer.SerializeToElement(original, JsonDefaults.Options);
        var decoded = operation.Decode(new Dictionary<string, JsonElement> { [Request] = publicRequest });
        await Assert.That(decoded.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(decoded.CommandKind).IsEqualTo(OperationKind.EventFeedControl);
        var actual = await McpNativePayloadAssertions.AssertTypedPublicPayload<EventFeedControlRequest>(publicRequest, decoded.Payload);
        var roundtrip = NativeSerialization.Deserialize<EventFeedControlRequest>(NativeSerialization.Serialize(actual));
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(publicRequest.GetRawText()),
            JsonNode.Parse(JsonSerializer.Serialize(roundtrip, JsonDefaults.Options)))).IsTrue();
    }

    private static async Task DenialsAsync(McpOperationDescriptor descriptor, EventFeedControlRequest original)
    {
        var wrongAction = new Dictionary<string, JsonElement>
        { [Request] = JsonSerializer.SerializeToElement(original with { Action = EventFeedControlAction.Open }, JsonDefaults.Options) };
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
