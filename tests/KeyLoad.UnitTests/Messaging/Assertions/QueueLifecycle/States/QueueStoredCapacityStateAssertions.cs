using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueStoredCapacityStateAssertions
{
    internal static async Task LeasedAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition, Delivery delivery)
    {
        var lane = new QueueLaneRef(partition, QueueEnqueueColdProtocol.Queue);
        await Assert.That(owner.InspectMessage(QueueEnqueueColdProtocol.Root, lane, QueueEnqueueColdProtocol.Ready))
            .IsEqualTo(new MessageInspection(new(QueueEnqueueColdProtocol.Ready, MessageState.Leased, QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.Second, QueueEnqueueAuthorityProtocol.First, null, null,
                QueueEnqueueColdProtocol.Root, QueueEnqueueAuthorityProtocol.First, delivery.LeaseUntil), QueueEnqueueColdProtocol.ReadyJson, QueueEnqueueColdProtocol.Headers));
        var bytes = OrleansSerialization.Serialize(new MessageBody(QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.ReadyJson,
            QueueEnqueueColdProtocol.Headers, null, JsonData.Fingerprint(new EnqueueMessage(lane.Queue, QueueEnqueueColdProtocol.Ready,
                QueueEnqueueColdProtocol.ReadyJson, QueueEnqueueColdProtocol.Headers)))).LongLength;
        var scheduled = store.Read(view => view.ReadOwnedValue(KeySpace.Partition(QueueEnqueueColdProtocol.BodyFamily, partition, lane.Queue, QueueEnqueueColdProtocol.Scheduled)))!;
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueEnqueueColdProtocol.CountersFamily, partition, lane.Queue))))
            .IsEqualTo(new QueueCounters(QueueEnqueueAuthorityProtocol.Second, bytes + scheduled.LongLength, QueueEnqueueAuthorityProtocol.First, bytes, QueueEnqueueAuthorityProtocol.First));
        await Assert.That(store.Read(view => view.Scan(KeySpace.Partition(QueueEnqueueColdProtocol.ReadyFamily, partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords).Records)).IsEmpty();
        var page = store.Read(view => view.Scan(KeySpace.Partition(QueueEnqueueAuthorityProtocol.LeaseFamily, partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords));
        await Assert.That(page.HasMore).IsFalse();
        var row = await Assert.That(page.Records).HasSingleItem();
        await Assert.That(row.Key.ToArray()).IsEquivalentTo(KeySpace.Partition(QueueEnqueueAuthorityProtocol.LeaseFamily, partition, lane.Queue, delivery.LeaseUntil,
            QueueEnqueueColdProtocol.Ready), CollectionOrdering.Matching);
        await Assert.That(OrleansSerialization.Deserialize<string>(row.Value.Span)).IsEqualTo(QueueEnqueueColdProtocol.Ready);
    }

    internal static async Task TerminalAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition)
    {
        var lane = new QueueLaneRef(partition, QueueEnqueueColdProtocol.Queue);
        await Assert.That(owner.InspectMessage(QueueEnqueueColdProtocol.Root, lane, QueueEnqueueColdProtocol.Ready))
            .IsEqualTo(new MessageInspection(new(QueueEnqueueColdProtocol.Ready, MessageState.Acked, QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.Settled, QueueEnqueueAuthorityProtocol.First, null, null, LeaseVersion: QueueEnqueueAuthorityProtocol.First), null, null));
        await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.Partition(QueueEnqueueColdProtocol.BodyFamily, partition, lane.Queue, QueueEnqueueColdProtocol.Ready)))).IsNull();
        await Assert.That(store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueEnqueueColdProtocol.ReadyFamily, partition, lane.Queue, QueueEnqueueAuthorityProtocol.FirstLong, QueueEnqueueColdProtocol.Ready)))).IsNull();
        await Assert.That(store.Read(view => view.Scan(KeySpace.Partition(QueueEnqueueAuthorityProtocol.LeaseFamily, partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords).Records)).IsEmpty();
    }
}
