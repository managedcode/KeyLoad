using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadOutboxImage
{
    private const int InventoryBound = 4096;
    private static readonly string[] Families = ["document", "document-epoch", "index", "unique",
        "stream-head", "event", "event-id", "event-feed", "event-sequence", "message-body", "message-meta",
        "queue-counters", "queue-order", "ready", "scheduled", "lease", "dead-letter", "inbox", "outbox", "outbox-head"];

    internal static string[] Domain(ZoneTreeStore store, PartitionRef partition) => store.Read(view =>
    {
        var rows = new List<string>();
        foreach (var family in Families)
        {
            var page = view.Scan(KeySpace.Partition(family, partition), InventoryBound);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete mixed-model image exceeded its execution bound.");
            }
            rows.AddRange(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)));
        }
        return rows.ToArray();
    });

    internal static async Task ExactAsync<T>(T actual, T expected)
    {
        if (actual is null || expected is null)
        {
            await Assert.That(actual is null && expected is null).IsTrue();
            return;
        }
        await Assert.That(OrleansSerialization.Serialize(actual))
            .IsEquivalentTo(OrleansSerialization.Serialize(expected), CollectionOrdering.Matching);
    }

    internal static async Task AbsentAsync(DatabaseEngine engine, PartitionRef partition)
    {
        await Assert.That(engine.GetDocument(AtomicTriadOutboxFixture.Root,
            new(partition, AtomicTriadOutboxFixture.Collection, AtomicTriadOutboxFixture.Entity))).IsNull();
        await Assert.That(engine.InspectMessage(AtomicTriadOutboxFixture.Root,
            new(partition, AtomicTriadOutboxFixture.Queue), AtomicTriadOutboxFixture.Entity)).IsNull();
        var stream = engine.ReadStream(AtomicTriadOutboxFixture.Root,
            new StreamRef(partition, AtomicTriadOutboxFixture.Streams, AtomicTriadOutboxFixture.Entity));
        await Assert.That(stream.Events).IsEmpty();
        await ExactAsync(stream.Head, new StreamHead(0L, 1L, 1L));
    }

    internal static async Task ReplayAsync(DatabaseEngine engine, ZoneTreeStore store,
        Command<DatabaseCommand> operation, Result<object> original, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        await ExactAsync(engine.ApplyEmbedded(operation, token), original);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store))
            .IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
