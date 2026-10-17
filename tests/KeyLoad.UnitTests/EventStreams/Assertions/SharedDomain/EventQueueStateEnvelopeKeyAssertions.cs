using System.Globalization;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeKeyAssertions
{
    private const string GoldenTenant = "tenant";
    private const string GoldenDatabase = "database";
    private const string GoldenDomain = "orders";
    private const string GoldenKey = "customer-1";
    private const string GoldenTimestamp = "2000-01-01T00:00:00+00:00";
    private const string OtherTenant = "tenant-other";
    private const string OtherDatabase = "database-other";
    private const string OtherDomain = "orders-other";
    private const string OtherKey = "customer-other";
    private static DateTimeOffset GoldenDeadline => DateTimeOffset.Parse(GoldenTimestamp, CultureInfo.InvariantCulture);
    private const string HeadHex =
        "015073747265616D2D6865616400005074656E616E7400005064617461626173650000506F72646572730000" +
        "50637573746F6D65722D31000050656E76656C6F70652D6576656E747300005073616D652D656E76656C6F70" +
        "652D69640000";
    private const string HeadSpace = "stream-head";
    private const string EventHex =
        "01506576656E7400005074656E616E7400005064617461626173650000506F7264657273000050637573746F" +
        "6D65722D31000050656E76656C6F70652D6576656E747300005073616D652D656E76656C6F70652D69640000" +
        "308000000000000001308000000000000001";
    private const string EventSpace = "event";
    private const string IdentityHex =
        "01506576656E742D696400005074656E616E7400005064617461626173650000506F72646572730000506375" +
        "73746F6D65722D31000050656E76656C6F70652D6576656E747300005073616D652D656E76656C6F70652D69" +
        "6400003080000000000000015073616D652D656E76656C6F70652D69640000";
    private const string IdentitySpace = "event-id";
    private const string FeedHex =
        "01506576656E742D6665656400005074656E616E7400005064617461626173650000506F7264657273000050" +
        "637573746F6D65722D310000308000000000000001";
    private const string FeedSpace = "event-feed";
    private const string SequenceHex =
        "01506576656E742D73657175656E636500005074656E616E7400005064617461626173650000506F72646572" +
        "73000050637573746F6D65722D310000";
    private const string SequenceSpace = "event-sequence";
    private const string TopicHeadHex =
        "0150746F7069632D6865616400005074656E616E7400005064617461626173650000506F7264657273000050" +
        "637573746F6D65722D31000050656E76656C6F70652D746F7069630000";
    private const string TopicHeadSpace = "topic-head";
    private const string TopicEventHex =
        "0150746F7069632D6576656E7400005074656E616E7400005064617461626173650000506F72646572730000" +
        "50637573746F6D65722D31000050656E76656C6F70652D746F70696300003080000000000000013080000000" +
        "00000001";
    private const string TopicEventSpace = "topic-event";
    private const string TopicIdentityHex =
        "0150746F7069632D6576656E742D696400005074656E616E7400005064617461626173650000506F72646572" +
        "73000050637573746F6D65722D31000050656E76656C6F70652D746F70696300003080000000000000015073" +
        "616D652D656E76656C6F70652D69640000";
    private const string TopicIdentitySpace = "topic-event-id";
    private const string BodyHex =
        "01506D6573736167652D626F647900005074656E616E7400005064617461626173650000506F726465727300" +
        "0050637573746F6D65722D31000050656E76656C6F70652D776F726B00005073616D652D656E76656C6F7065" +
        "2D69640000";
    private const string BodySpace = "message-body";
    private const string MetadataHex =
        "01506D6573736167652D6D65746100005074656E616E7400005064617461626173650000506F726465727300" +
        "0050637573746F6D65722D31000050656E76656C6F70652D776F726B00005073616D652D656E76656C6F7065" +
        "2D69640000";
    private const string MetadataSpace = "message-meta";
    private const string ReadyHex =
        "0150726561647900005074656E616E7400005064617461626173650000506F7264657273000050637573746F" +
        "6D65722D31000050656E76656C6F70652D776F726B00003080000000000000015073616D652D656E76656C6F" +
        "70652D69640000";
    private const string ReadySpace = "ready";
    private const string ParkedHex =
        "0150646561642D6C657474657200005074656E616E7400005064617461626173650000506F72646572730000" +
        "50637573746F6D65722D31000050656E76656C6F70652D776F726B00005073616D652D656E76656C6F70652D" +
        "69640000";
    private const string ParkedSpace = "dead-letter";
    private const string EdgeHex =
        "01506564676500005074656E616E7400005064617461626173650000506F7264657273000050637573746F6D" +
        "65722D31000050656E76656C6F70652D677261706800005073616D652D656E76656C6F70652D69640000";
    private const string EdgeSpace = "edge";
    private const string GroupHex =
        "0150737562736372697074696F6E00005074656E616E7400005064617461626173650000506F726465727300" +
        "0050637573746F6D65722D31000050656E76656C6F70652D746F706963000050546F70696300001130800000" +
        "000000000150656E76656C6F70652D67726F75700000";
    private const string GroupSpace = "subscription";
    private const string WindowHex =
        "0150737562736372697074696F6E2D77696E646F7700005074656E616E740000506461746162617365000050" +
        "6F7264657273000050637573746F6D65722D31000050656E76656C6F70652D746F706963000050546F706963" +
        "00001130800000000000000150656E76656C6F70652D67726F75700000308000000000000001308000000000" +
        "000001";
    private const string WindowSpace = "subscription-window";
    private const string CompletionHex =
        "0150737562736372697074696F6E2D636F6D706C6574696F6E00005074656E616E7400005064617461626173" +
        "650000506F7264657273000050637573746F6D65722D31000050656E76656C6F70652D746F70696300005054" +
        "6F70696300001130800000000000000150656E76656C6F70652D67726F757000003080000000000000013080" +
        "00000000000001";
    private const string CompletionSpace = "subscription-completion";
    private const string InboxHex =
        "0150696E626F7800005074656E616E7400005064617461626173650000506F7264657273000050637573746F" +
        "6D65722D31000050656E76656C6F70652D776F726B00005073616D652D656E76656C6F70652D696400005065" +
        "6E76656C6F70652D68616E646C65720000308000000000000001";
    private const string InboxSpace = "inbox";
    private const string ScheduledHex =
        "01507363686564756C656400005074656E616E7400005064617461626173650000506F726465727300005063" +
        "7573746F6D65722D31000050656E76656C6F70652D776F726B00004088C1220247E440005073616D652D656E" +
        "76656C6F70652D69640000";
    private const string ScheduledSpace = "scheduled";
    private const string LeaseHex =
        "01506C6561736500005074656E616E7400005064617461626173650000506F7264657273000050637573746F" +
        "6D65722D31000050656E76656C6F70652D776F726B00004088C1220247E440005073616D652D656E76656C6F" +
        "70652D69640000";
    private const string LeaseSpace = "lease";

    internal static async Task CurrentAsync(EventQueueStateEnvelopeFixture fixture)
    {
        await Assert.That(fixture.Partition).IsEqualTo(new PartitionRef(GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey));
        await KeyAsync(KeySpace.Partition(HeadSpace, fixture.Partition, Streams, Item),
            [HeadSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Streams, Item], HeadHex);
        await KeyAsync(KeySpace.Partition(EventSpace, fixture.Partition, Streams, Item, InitialGeneration, InitialRevision),
            [EventSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Streams, Item, InitialGeneration, InitialRevision], EventHex);
        await KeyAsync(KeySpace.Partition(IdentitySpace, fixture.Partition, Streams, Item, InitialGeneration, Item),
            [IdentitySpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Streams, Item, InitialGeneration, Item], IdentityHex);
        await KeyAsync(KeySpace.Partition(FeedSpace, fixture.Partition, InitialRevision),
            [FeedSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, InitialRevision], FeedHex);
        await KeyAsync(KeySpace.Partition(SequenceSpace, fixture.Partition),
            [SequenceSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey], SequenceHex);
        await KeyAsync(KeySpace.Partition(TopicHeadSpace, fixture.Partition, Topic),
            [TopicHeadSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Topic], TopicHeadHex);
        await KeyAsync(KeySpace.Partition(TopicEventSpace, fixture.Partition, Topic, InitialGeneration, InitialRevision),
            [TopicEventSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Topic, InitialGeneration, InitialRevision], TopicEventHex);
        await KeyAsync(KeySpace.Partition(TopicIdentitySpace, fixture.Partition, Topic, InitialGeneration, Item),
            [TopicIdentitySpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Topic, InitialGeneration, Item], TopicIdentityHex);
        await KeyAsync(KeySpace.Partition(BodySpace, fixture.Partition, Queue, Item),
            [BodySpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Queue, Item], BodyHex);
        await KeyAsync(KeySpace.Partition(MetadataSpace, fixture.Partition, Queue, Item),
            [MetadataSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Queue, Item], MetadataHex);
        await KeyAsync(KeySpace.Partition(ReadySpace, fixture.Partition, Queue, InitialRevision, Item),
            [ReadySpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Queue, InitialRevision, Item], ReadyHex);
        await KeyAsync(KeySpace.Partition(ParkedSpace, fixture.Partition, Queue, Item),
            [ParkedSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Queue, Item], ParkedHex);
        await KeyAsync(KeySpace.Partition(EdgeSpace, fixture.Partition, Graph, Item),
            [EdgeSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Graph, Item], EdgeHex);
        await KeyAsync(KeySpace.Partition(GroupSpace, fixture.Partition, Topic, nameof(EventSourceKind.Topic), null, InitialGeneration, Group),
            [GroupSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Topic, nameof(EventSourceKind.Topic), null, InitialGeneration, Group], GroupHex);
        await KeyAsync(KeySpace.Partition(WindowSpace, fixture.Partition, Topic, nameof(EventSourceKind.Topic), null, InitialGeneration, Group, InitialGeneration, InitialRevision),
            [WindowSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Topic, nameof(EventSourceKind.Topic), null, InitialGeneration, Group, InitialGeneration, InitialRevision], WindowHex);
        await KeyAsync(KeySpace.Partition(CompletionSpace, fixture.Partition, Topic, nameof(EventSourceKind.Topic), null, InitialGeneration, Group, InitialGeneration, InitialRevision),
            [CompletionSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Topic, nameof(EventSourceKind.Topic), null, InitialGeneration, Group, InitialGeneration, InitialRevision], CompletionHex);
        await KeyAsync(KeySpace.Partition(InboxSpace, fixture.Partition, Queue, Item, Handler, InitialGeneration),
            [InboxSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Queue, Item, Handler, InitialGeneration], InboxHex);
        await KeyAsync(KeySpace.Partition(ScheduledSpace, fixture.Partition, Queue, GoldenDeadline, Item),
            [ScheduledSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Queue, GoldenDeadline, Item], ScheduledHex);
        await KeyAsync(KeySpace.Partition(LeaseSpace, fixture.Partition, Queue, GoldenDeadline, Item),
            [LeaseSpace, GoldenTenant, GoldenDatabase, GoldenDomain, GoldenKey, Queue, GoldenDeadline, Item], LeaseHex);
        await ScopeAndOrderAsync(fixture.Partition);
    }

    private static async Task KeyAsync(byte[] actual, object?[] components, string expectedHex)
    {
        await Assert.That(Convert.ToHexString(actual)).IsEqualTo(expectedHex);
        var decoded = KeyCodec.Decode(actual);
        await Assert.That(JsonSerializer.Serialize(decoded, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(components, JsonDefaults.Options));
        await Assert.That(KeyCodec.Encode(decoded).AsSpan().SequenceEqual(actual)).IsTrue();
    }

    private static async Task ScopeAndOrderAsync(PartitionRef partition)
    {
        var first = KeySpace.Partition(ReadySpace, partition, Queue, ReadySequence, Item);
        var later = KeySpace.Partition(ReadySpace, partition, Queue, ScheduledReadySequence, Item);
        await Assert.That(first.AsSpan().SequenceCompareTo(later)).IsLessThan(NoUsage);
        foreach (var other in new[]
        {
            partition with { TenantId = OtherTenant },
            partition with { DatabaseId = OtherDatabase },
            partition with { TransactionDomainId = OtherDomain },
            partition with { PartitionKey = OtherKey }
        })
        {
            await Assert.That(first.AsSpan().SequenceEqual(
                KeySpace.Partition(ReadySpace, other, Queue, ReadySequence, Item))).IsFalse();
        }
    }
}
