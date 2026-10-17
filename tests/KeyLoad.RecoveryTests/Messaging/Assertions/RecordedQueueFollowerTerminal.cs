using KeyLoad.Core;
using static KeyLoad.RecoveryTests.Features.Messaging.RecordedQueueFollowerProtocol;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RecordedQueueFollowerTerminal
{
    internal static async Task RequireAsync(PeerDiscoveryNativeCluster cluster)
    {
        var original = new MessageMetadata(Message, MessageState.Acked, FirstAttempt, AckedStateVersion,
            FirstReadySequence, null, null, LeaseVersion: InitialLeaseVersion, DeliveryGeneration: InitialDeliveryGeneration);
        var healthy = new MessageMetadata(HealthyMessage, MessageState.Acked, FirstAttempt, HealthyAckedStateVersion,
            HealthyReadySequence, null, null, LeaseVersion: InitialLeaseVersion, DeliveryGeneration: InitialDeliveryGeneration);
        var counters = new QueueCounters(NoUsage, NoUsage, NoUsage, NoUsage, HealthyReadySequence);
        await RecordedQueueFollowerAssertions.StateAsync(cluster, original, null, counters);
        await RecordedQueueFollowerAssertions.StateAsync(cluster, healthy, null, counters);
        foreach (var node in cluster.Nodes)
        {
            var prefix = KeySpace.Partition(MetadataFamily, Partition, Queue);
            var page = node.Database.Store.Read(view => view.Scan(prefix, ImageRecords));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records.Select(row => NativeSerialization.Deserialize<MessageMetadata>(row.Value.Span)))
                .IsEquivalentTo(new[] { original, healthy });
            var bodies = node.Database.Store.Read(view => view.Scan(KeySpace.Partition(BodyFamily, Partition, Queue), ImageRecords));
            await Assert.That(bodies.HasMore).IsFalse();
            await Assert.That(bodies.Records).IsEmpty();
            foreach (var record in page.Records)
            {
                await Assert.That(NativeSerialization.Deserialize<MessageMetadata>(record.Value.Span).ExpiresAt).IsNull();
            }
            foreach (var family in new[] { PendingFamily, ParkedFamily, DeadLetterFamily, QueueOrderFamily, InboxFamily })
            {
                var references = node.Database.Store.Read(view => view.Scan(KeySpace.Partition(family, Partition, Queue), ImageRecords));
                await Assert.That(references.HasMore).IsFalse();
                await Assert.That(references.Records).IsEmpty();
            }
        }
    }
}
