using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;
namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class TopicPurgeModelAssertions
{
    internal static async Task VerifyAsync(DatabaseEngine database, ReplicatedOperation publication, long first, bool pinned, string cursor)
    {
        var store = database.Store;
        var records = Enumerable.Range((int)first, 4 - (int)first).Select(i =>
            new SourceEventRecord(TopicPurgeCrashContract.Source, i, i, TopicPurgeCrashContract.Event(i), publication.EvaluatedAt)).ToArray();
        var head = store.Read(view => view.GetRecord<TopicHead>(KeySpace.Partition("topic-head", TopicPurgeCrashContract.Partition, TopicPurgeCrashContract.Topic)));
        await Assert.That(head).IsEqualTo(new TopicHead(3, first, 1, records.Sum(record => NativeSerialization.Serialize(record).LongLength)));
        var page = database.ReadEventSource("root", new(TopicPurgeCrashContract.Source, AfterPosition: first - 1));
        await Assert.That(page.Events).IsEquivalentTo(records, CollectionOrdering.Matching);
        await Assert.That(page.HasMore).IsFalse();
        for (var position = 1L; position <= 3; position++)
        { await IdentityAsync(store, position, position < first); }
        var pin = database.GetSubscription("root", TopicPurgeCrashContract.Subscription);
        await Assert.That(pin.Checkpoint).IsEqualTo(pinned ? 1L : 3L);
        await Assert.That(pin.Generation).IsEqualTo(1L);
        await Assert.That(pin.Paused).IsTrue();
        var doc = database.GetDocument("root", new(TopicPurgeCrashContract.Partition, TopicPurgeCrashContract.Documents, "retained"))!;
        await Assert.That(doc.Json).IsEqualTo(TopicPurgeCrashContract.DocumentJson);
        await Assert.That(doc.Revision).IsEqualTo(1L);
        var queue = database.InspectMessage("root", TopicPurgeCrashContract.Lane, "retained")!;
        await Assert.That(queue).IsEqualTo(new MessageInspection(new("retained", MessageState.Ready, 0, 1, 1, null, null),
            TopicPurgeCrashContract.QueueJson, TopicPurgeCrashContract.HeadersJson));
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadEventSource("root", new(TopicPurgeCrashContract.Source)));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(missing.Message).IsEqualTo("The requested event history was retained away.");
        if (first == 3)
        {
            var cursorLost = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadEventSource("root", new(TopicPurgeCrashContract.Source, Cursor: cursor)));
            await Assert.That(cursorLost.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        }
    }
    private static async Task IdentityAsync(IAtomicStore store, long position, bool removed)
    {
        var id = "event" + position.ToString(CultureInfo.InvariantCulture);
        var record = KeySpace.Partition("topic-event", TopicPurgeCrashContract.Partition, TopicPurgeCrashContract.Topic, 1L, position);
        var pointer = KeySpace.Partition("topic-event-id", TopicPurgeCrashContract.Partition, TopicPurgeCrashContract.Topic, 1L, id);
        var digest = KeySpace.Partition("topic-event-id", TopicPurgeCrashContract.Partition, TopicPurgeCrashContract.Topic, 1L, id, "retained-digest");
        await Assert.That(store.Read(view => view.ReadOwnedValue(record)) is null).IsEqualTo(removed);
        await Assert.That(store.Read(view => view.ReadOwnedValue(pointer)) is null).IsEqualTo(removed);
        var identity = store.Read(view => view.GetRecord<RetainedTopicEventIdentity>(digest));
        if (!removed)
        { await Assert.That(identity).IsNull(); return; }
        await Assert.That(identity!.Position).IsEqualTo(position);
        await Assert.That(identity.Generation).IsEqualTo(1L);
        var canonical = """{"causationId":null,"correlationId":null,"eventId":"eventPOSITION","eventType":"Created","headersJson":"{}","occurredAt":null,"payloadJson":"{\u0022n\u0022:POSITION}","schemaVersion":1}""";
        var json = canonical.Replace("POSITION", position.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        await Assert.That(identity.ContentDigest).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
    }
}
