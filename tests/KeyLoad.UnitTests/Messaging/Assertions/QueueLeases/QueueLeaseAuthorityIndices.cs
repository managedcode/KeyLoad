using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLeaseAuthorityIndices
{
    internal static async Task RequireAsync(ZoneTreeStore store, QueueLeaseAuthorityState state, MessageMetadata metadata)
    {
        await ReferenceAsync(store, state.Lane, QueueLeaseAuthorityProtocol.LeaseSpace,
            metadata.State == MessageState.Leased ? metadata.LeaseUntil : null, state.Message.MessageId);
        await ReferenceAsync(store, state.Lane, QueueLeaseAuthorityProtocol.ScheduledSpace,
            metadata.State == MessageState.Scheduled ? metadata.NotBefore : null, state.Message.MessageId);
        var ready = store.Read(view => view.Scan(KeySpace.Partition(QueueLeaseAuthorityProtocol.ReadySpace,
            state.Lane.Partition, state.Lane.Queue), QueueLeaseAuthorityProtocol.Two));
        await Assert.That(ready.HasMore).IsFalse();
        await Assert.That(ready.Records).IsEmpty();
    }

    private static async Task ReferenceAsync(ZoneTreeStore store, QueueLaneRef lane, string family,
        DateTimeOffset? deadline, string messageId)
    {
        var page = store.Read(view => view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), QueueLeaseAuthorityProtocol.Two));
        await Assert.That(page.HasMore).IsFalse();
        var actual = page.Records.Select(row => Convert.ToHexString(row.Key.Span)
            + QueueLeaseAuthorityProtocol.RowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();
        string[] expected = deadline is { } time
            ? [Convert.ToHexString(KeySpace.Partition(family, lane.Partition, lane.Queue, time, messageId))
                + QueueLeaseAuthorityProtocol.RowSeparator + Convert.ToHexString(OrleansSerialization.Serialize(messageId))] : [];
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}
