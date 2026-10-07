using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class EventAppendDedupAssertions
{
    private const string DuplicateDetail = "The event ID is already retained in this source generation.";
    private const string ConflictDetail = "The event ID was reused with different content.";
    private const string ChangedEventJson = "{\"event\":false}";
    private static readonly string[] DomainFamilies = ["document", "document-epoch", "stream-head", "event", "event-id",
        "event-feed", "event-sequence", "message-body", "message-meta", "queue-counters", "ready", "outbox", "outbox-head"];
    private const string ByteSeparator = ":";
    private const int MaximumFixtureRecords = 4_096;
    private const string Overflow = "The append domain exceeded its fixed fixture bound.";

    internal static async Task VerifyAsync(DatabaseEngine database, DateTimeOffset time, long seedTail)
    {
        var before = DomainBytes(database);
        foreach (var changed in new[] { false, true })
        {
            var id = Guid.NewGuid();
            var item = EventAppendCrashContract.Event(EventAppendCrashContract.EventId);
            if (changed)
            { item = item with { PayloadJson = ChangedEventJson }; }
            var command = new CommandRequest(id, EventAppendCrashContract.Partition,
                [new AppendEvents(EventAppendCrashContract.Streams, EventAppendCrashContract.StreamId, [item], ExpectedStreamRevision.Any)]);
            var original = new ReplicatedOperation(id, OperationKind.Batch, EventAppendCrashContract.Principal,
                time, JsonSerializer.Serialize(command, JsonDefaults.Options));
            var beforePosition = database.Store.Position;
            var rejected = database.Apply(original);
            await Assert.That(database.Store.Position).IsEqualTo(beforePosition + 1);
            await Assert.That(rejected.Error).IsEqualTo(changed ? ErrorCode.Conflict : ErrorCode.DuplicateEventId);
            await Assert.That(rejected.SafeDetail).IsEqualTo(changed ? ConflictDetail : DuplicateDetail);
            var afterRejectionPosition = database.Store.Position;
            var rejectedBytes = EventAppendStateAssertions.Bytes(database.Store);
            await EventAppendReceiptAssertions.SameResultAsync(rejected, database.Apply(original));
            await Assert.That(database.Store.Position).IsEqualTo(afterRejectionPosition);
            await EventAppendStateAssertions.SameBytesAsync(rejectedBytes, database.Store);
            await Assert.That(DomainBytes(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        }
        await EventAppendStateAssertions.VerifyAsync(database, time, seedTail, committed: true, healthy: false);
    }

    private static string[] DomainBytes(DatabaseEngine database) => database.Store.Read(view => DomainFamilies.SelectMany(family =>
    {
        var page = view.Scan(KeySpace.Partition(family, EventAppendCrashContract.Partition), MaximumFixtureRecords);
        if (page.HasMore)
        { throw new InvalidOperationException(Overflow); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ByteSeparator + Convert.ToHexString(record.Value.Span));
    }).ToArray());
}
