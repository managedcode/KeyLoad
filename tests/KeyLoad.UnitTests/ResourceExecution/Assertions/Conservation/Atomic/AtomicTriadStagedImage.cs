using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class AtomicTriadStagedImage
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
}
