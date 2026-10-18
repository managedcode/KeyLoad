using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.Messaging.RemoteTransferCapacityAuthorityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferCapacityAuthorityImages
{
    internal static async Task CompletedAsync(RemoteTransferDatabase fixture, RemoteTransferCapacityAuthorityCompleted item)
    {
        var transfer = item.Transfer;
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transfer.Mutation.TransferId), transfer.Intent with
            {
                State = QueueTransferState.Delivered,
                ReceiptToken = item.Proof.ReceiptToken
            });
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.Mutation.TransferId), item.Proof);
        var metadata = new MessageMetadata(transfer.Mutation.Message.MessageId, MessageState.Acked, First, Third,
            item.ReadySequence, null, null, LeaseVersion: First);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, transfer.Mutation.Message.MessageId), new MessageInspection(metadata, null, null));
        var body = KeySpace.Partition(Body, fixture.DestinationPartition, fixture.DestinationQueue.Queue, metadata.Id);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(body))).IsNull();
        await NativeRowsAsync(fixture, item);
    }

    private static async Task NativeRowsAsync(RemoteTransferDatabase fixture, RemoteTransferCapacityAuthorityCompleted item)
    {
        var transfer = item.Transfer;
        var placement = fixture.Database.ReadAtomicPartitionPlacement(RemoteTransferDatabase.RootPrincipal, fixture.DestinationPartition);
        var maximum = new CommitToken(placement.Incarnation, fixture.DestinationPartition.AtomicPartitionId, long.MaxValue, placement.PlacementEpoch);
        var fingerprint = JsonData.Fingerprint(transfer.Mutation.Message);
        var verified = fixture.Database.Verify<RemoteTransferIntentClaims>(transfer.Intent.IntentToken);
        var maximumClaims = new RemoteTransferReceiptClaims(RemoteTransferProtocol.ReceiptPurpose, fixture.Store.Identity.Incarnation,
            verified.Source, verified.TransferId, verified.Destination, verified.PrincipalId, verified.Fingerprint, maximum);
        var reservation = System.Text.Encoding.UTF8.GetByteCount(fixture.Database.Sign(maximumClaims));
        var source = new RemoteTransferIntentRecord(fixture.SourceQueue, transfer.Mutation.TransferId, fixture.DestinationQueue,
            RemoteTransferDatabase.RootPrincipal, transfer.Mutation.Message, fingerprint, QueueTransferState.Delivered,
            transfer.Intent.IntentToken, item.Proof.ReceiptToken, reservation);
        var target = new RemoteTransferTargetReceiptRecord(fixture.SourceQueue, transfer.Mutation.TransferId, fixture.DestinationQueue,
            RemoteTransferDatabase.RootPrincipal, fingerprint, item.Proof.ReceiptToken, item.Proof.TargetCommit);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Store.Read(view => view.GetRecord<RemoteTransferIntentRecord>(
            RemoteTransferStorage.IntentKey(fixture.SourceQueue, transfer.Mutation.TransferId))), source);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Store.Read(view => view.GetRecord<RemoteTransferTargetReceiptRecord>(
            RemoteTransferStorage.TargetReceiptKey(fixture.SourceQueue, transfer.Mutation.TransferId, fixture.DestinationQueue))), target);
    }

    internal static async Task AllAsync(RemoteTransferDatabase fixture, IReadOnlyList<RemoteTransferCapacityAuthorityCompleted> completed)
    {
        foreach (var item in completed)
        {
            await CompletedAsync(fixture, item);
        }
        await MetadataAsync(fixture, completed);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition(Counters, fixture.DestinationPartition, fixture.DestinationQueue.Queue))),
            new QueueCounters(NoUsage, NoUsage, NoUsage, NoUsage, completed.Count));
        foreach (var family in new[] { Body, Ready, Lease, Scheduled, PendingDeadLetter, Parked, DeadLetter, QueueOrder })
        {
            var page = fixture.Store.Read(view => view.Scan(KeySpace.Partition(family, fixture.DestinationPartition,
                fixture.DestinationQueue.Queue), ImageBound));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records).IsEmpty();
        }
        await CounterAsync(fixture, true, completed.Count);
        await CounterAsync(fixture, false, completed.Count);
    }

    private static async Task MetadataAsync(RemoteTransferDatabase fixture,
        IReadOnlyList<RemoteTransferCapacityAuthorityCompleted> completed)
    {
        var page = fixture.Store.Read(view => view.Scan(KeySpace.Partition(Metadata, fixture.DestinationPartition,
            fixture.DestinationQueue.Queue), ImageBound));
        await Assert.That(page.HasMore).IsFalse();
        var expected = completed.Select(item =>
        {
            var id = item.Transfer.Mutation.Message.MessageId;
            var key = KeySpace.Partition(Metadata, fixture.DestinationPartition, fixture.DestinationQueue.Queue, id);
            var value = new MessageMetadata(id, MessageState.Acked, First, Third,
                item.ReadySequence, null, null, LeaseVersion: First);
            return Convert.ToHexString(key) + ":" + Convert.ToHexString(NativeSerialization.Serialize(value));
        });
        await Assert.That(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).Order())
            .IsEquivalentTo(expected.Order(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task CounterAsync(RemoteTransferDatabase fixture, bool source, long count)
    {
        var lane = source ? fixture.SourceQueue : fixture.DestinationQueue;
        var prefix = KeySpace.Partition(source ? RemoteTransferProtocol.IntentSpace : RemoteTransferProtocol.TargetReceiptSpace,
            lane.Partition, lane.Queue);
        var page = fixture.Store.Read(view => view.Scan(prefix, ImageBound));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(checked((int)count));
        long charged = NoUsage;
        foreach (var row in page.Records)
        {
            var extra = NoUsage;
            if (source)
            {
                var record = NativeSerialization.Deserialize<RemoteTransferIntentRecord>(row.Value.Span);
                extra = record.ReceiptReservationBytes - System.Text.Encoding.UTF8.GetByteCount(record.ReceiptToken!);
            }
            charged += row.Value.Length + extra;
        }
        await RemoteTransferCapacityAuthorityValues.EqualAsync(source ? fixture.SourceCapacity(lane) : fixture.TargetCapacity(lane),
            new RemoteTransferCapacity(count, charged));
    }
}
