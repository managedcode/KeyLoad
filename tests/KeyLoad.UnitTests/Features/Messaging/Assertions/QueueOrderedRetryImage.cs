using KeyLoad.Core;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryImage
{
    private static readonly string[] Families = ["ready", "scheduled", "lease", "message-body", "message-meta", "queue-counters",
        "dead-letter", "queue-pending-dead-letter", "queue-dead-letter-order", "queue-order"];
    internal static string[] Capture(IAtomicStore store, QueueLaneRef lane) => store.Read(view => Families.SelectMany(family =>
    {
        var page = view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), QueueLifecycleTestProtocol.ImageRecords);
        if (page.HasMore)
        { throw new InvalidOperationException("The ordered queue fixture image exceeds its original bound."); }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
    }).ToArray());
    internal static async Task SameAsync(IAtomicStore store, QueueLaneRef lane, string[] expected)
        => await Assert.That(Capture(store, lane)).IsEquivalentTo(expected, CollectionOrdering.Matching);
}
