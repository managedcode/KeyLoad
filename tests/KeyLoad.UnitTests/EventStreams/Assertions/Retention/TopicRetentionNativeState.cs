using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class TopicRetentionNativeState
{
    internal static async Task VerifyAsync(TopicRetentionFixture fixture)
    {
        var expectedRecord = new SourceEventRecord(fixture.Source, 3, 3,
            new("event3", "Created", "{\"n\":3}"), fixture.PublicationAt);
        var expectedBytes = NativeSerialization.Serialize(expectedRecord).LongLength;
        var head = fixture.Store.Read(view => view.GetRecord<TopicHead>(
            KeySpace.Partition("topic-head", fixture.Owner.Partition, "topic")));
        await Assert.That(head).IsEqualTo(new TopicHead(3, 3, 1, expectedBytes));
        foreach (var position in new[] { 1L, 2L })
        {
            var id = "event" + position;
            var recordKey = KeySpace.Partition("topic-event", fixture.Owner.Partition, "topic", 1L, position);
            var pointer = KeySpace.Partition("topic-event-id", fixture.Owner.Partition, "topic", 1L, id);
            await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(recordKey))).IsNull();
            await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(pointer))).IsNull();
            var key = KeySpace.Partition("topic-event-id", fixture.Owner.Partition, "topic", 1L, id, "retained-digest");
            var identity = fixture.Store.Read(view => view.GetRecord<RetainedTopicEventIdentity>(key));
            await Assert.That(identity!.Position).IsEqualTo(position);
            await Assert.That(identity.Generation).IsEqualTo(1L);
            var canonical = """{"causationId":null,"correlationId":null,"eventId":"eventPOSITION","eventType":"Created","headersJson":"{}","occurredAt":null,"payloadJson":"{\u0022n\u0022:POSITION}","schemaVersion":1}""";
            var bytes = Encoding.UTF8.GetBytes(canonical.Replace("POSITION", position.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            await Assert.That(identity.ContentDigest).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }
}
