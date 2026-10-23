using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleCeilingTerminalIndices
{
    private const int TerminalMessageCount = 4;
    private const string MetadataFamily = "message-meta";
    private static readonly string[] Families =
    ["ready", "scheduled", "lease", "message-body", "dead-letter", "queue-pending-dead-letter",
        "queue-dead-letter-order", "queue-order", "inbox"];

    internal static async Task EmptyAsync(ZoneTreeStore store, QueueLaneRef lane, int expectedMessages = TerminalMessageCount)
    {
        var metadata = store.Read(view => view.Scan(KeySpace.Partition(MetadataFamily, lane.Partition, lane.Queue),
            expectedMessages + 1));
        await Assert.That(metadata.Records.Length).IsEqualTo(expectedMessages);
        await Assert.That(metadata.HasMore).IsFalse();
        foreach (var family in Families)
        {
            var page = store.Read(view => view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), 1));
            await Assert.That(page.Records).IsEmpty();
            await Assert.That(page.HasMore).IsFalse();
        }
    }
}
