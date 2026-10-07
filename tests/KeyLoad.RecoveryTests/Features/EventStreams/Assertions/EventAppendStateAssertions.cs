using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class EventAppendStateAssertions
{
    private const string EventIdentitySpace = "event-id";
    private const string ByteSeparator = ":";
    private const int MaximumFixtureRecords = 4_096;
    private const string FixtureOverflow = "The seeded append state exceeded its bounded fixture inventory.";

    internal static async Task VerifyAsync(DatabaseEngine database, DateTimeOffset time, long seedTail,
        bool committed, bool healthy)
    {
        await EventAppendQueueAssertions.VerifyAsync(database.Store, committed, healthy);
        var page = database.ReadStream(EventAppendCrashContract.Principal, EventAppendCrashContract.Stream);
        var ids = new List<string> { EventAppendCrashContract.SeedId };
        if (committed)
        { ids.Add(EventAppendCrashContract.EventId); }
        if (healthy)
        { ids.Add(EventAppendCrashContract.Healthy); }
        await Assert.That(page.Stream).IsEqualTo(EventAppendCrashContract.Stream);
        await Assert.That(page.Head).IsEqualTo(new StreamHead(ids.Count, 1, 1));
        await Assert.That(page.CutPosition).IsEqualTo(database.Store.Position);
        await Assert.That(page.HasMore).IsFalse();
        var expected = ids.Select((id, index) => new EventRecord(EventAppendCrashContract.Stream,
            index + 1L, index + 1L, EventAppendCrashContract.Event(id), time));
        await Assert.That(page.Events.Select(value => JsonSerializer.Serialize(value, JsonDefaults.Options)))
            .IsEquivalentTo(expected.Select(value => JsonSerializer.Serialize(value, JsonDefaults.Options)), CollectionOrdering.Matching);
        await AssertProducerAsync(database, EventAppendCrashContract.Producer, committed, 1);
        await AssertProducerAsync(database, EventAppendCrashContract.Healthy, healthy, 2);
        await AssertIdentityAsync(database.Store, EventAppendCrashContract.SeedId, present: true);
        await AssertIdentityAsync(database.Store, EventAppendCrashContract.EventId, committed);
        await AssertIdentityAsync(database.Store, EventAppendCrashContract.Healthy, healthy);
        await Assert.That(database.GetOutboxStatus(EventAppendCrashContract.Principal,
            EventAppendCrashContract.Partition).Head.Tail).IsEqualTo(seedTail + (committed ? 3 : 0) + (healthy ? 3 : 0));
    }

    private static async Task AssertProducerAsync(DatabaseEngine database, string id, bool present, long readySequence)
    {
        var reference = new EntityRef(EventAppendCrashContract.Partition, EventAppendCrashContract.Documents, id);
        var document = database.GetDocument(EventAppendCrashContract.Principal, reference);
        var message = database.InspectMessage(EventAppendCrashContract.Principal, EventAppendCrashContract.Lane, id);
        if (!present)
        { await Assert.That(document).IsNull(); await Assert.That(message).IsNull(); return; }
        await Assert.That(document!.Reference).IsEqualTo(reference);
        await Assert.That(document.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(EventAppendCrashContract.DocumentJson);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        var expected = new MessageInspection(new(id, MessageState.Ready, 0, 1, readySequence, null, null),
            EventAppendCrashContract.PayloadJson, EventAppendCrashContract.HeadersJson);
        await Assert.That(JsonSerializer.Serialize(message, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    private static async Task AssertIdentityAsync(IAtomicStore store, string id, bool present)
    {
        var bytes = store.Read(view => view.ReadOwnedValue(KeySpace.Partition(EventIdentitySpace,
            EventAppendCrashContract.Partition, EventAppendCrashContract.Streams, EventAppendCrashContract.StreamId, 1L, id)));
        await Assert.That(bytes is not null).IsEqualTo(present);
    }

    internal static string[] Bytes(IAtomicStore store) => store.Read(view =>
    {
        var page = view.Scan([], MaximumFixtureRecords);
        if (page.HasMore)
        { throw new InvalidOperationException(FixtureOverflow); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ByteSeparator +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static async Task SameBytesAsync(string[] expected, IAtomicStore store)
        => await Assert.That(Bytes(store)).IsEquivalentTo(expected, CollectionOrdering.Matching);
}
