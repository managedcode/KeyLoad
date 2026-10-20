using System.Collections.Immutable;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleCeilingInjection
{
    private const string BodyFamily = "message-body";
    private const string MetadataFamily = "message-meta";
    private const string CounterFamily = "queue-counters";
    private const string ParkedFamily = "queue-dead-letter-order";
    private const string PendingFamily = "queue-pending-dead-letter";
    private const string OrderFamily = "queue-order";
    private const long NoOrderPosition = 0;
    private const string MissingFixture = "The original execution lifecycle ceiling fixture row is missing.";

    internal static QueueLifecycleCeilingRows Inject(ZoneTreeStore store, QueueLaneRef lane, string id,
        QueueLifecycleCeilingFault fault)
    {
        var changes = new List<KeyValueRecord>();
        var saved = ImmutableArray.CreateBuilder<KeyValueRecord>();
        var result = store.Read(view =>
        {
            var metadataKey = KeySpace.Partition(MetadataFamily, lane.Partition, lane.Queue, id);
            var metadataBytes = Require(view, metadataKey);
            var metadata = OrleansSerialization.Deserialize<MessageMetadata>(metadataBytes);
            var counterKey = KeySpace.Partition(CounterFamily, lane.Partition, lane.Queue);
            var counters = OrleansSerialization.Deserialize<QueueCounters>(Require(view, counterKey));
            var changed = Metadata(metadata, fault);
            if (changed != metadata)
            {
                Save(view, metadataKey, OrleansSerialization.Serialize(changed), saved, changes);
            }
            var changedCounters = Counters(counters, fault);
            if (changedCounters != counters)
            {
                Save(view, counterKey, OrleansSerialization.Serialize(changedCounters), saved, changes);
            }
            if (changed.DeliveryGeneration != metadata.DeliveryGeneration)
            {
                ChangeReferences(view, lane, metadata, saved, changes);
            }
            var request = new CommandRequest(Guid.NewGuid(), lane.Partition,
                [Mutation(lane, changed, fault)]);
            return new QueueLifecycleCeilingRows(lane, metadata, counters, saved.ToImmutable(), request);
        });
        store.Commit((transaction, _) =>
        {
            foreach (var change in changes)
            {
                transaction.Put(change.Key.ToArray(), change.Value.ToArray());
            }
            return true;
        });
        return result;
    }

    internal static void Repair(ZoneTreeStore store, QueueLifecycleCeilingRows rows)
    {
        store.Commit((transaction, _) =>
        {
            foreach (var row in rows.OriginalRows)
            {
                transaction.Put(row.Key.ToArray(), row.Value.ToArray());
            }
            return true;
        });
    }

    internal static bool IsPark(QueueLifecycleCeilingFault fault)
        => fault is QueueLifecycleCeilingFault.ParkStateVersion or QueueLifecycleCeilingFault.ParkSequence;

    internal static bool IsCancel(QueueLifecycleCeilingFault fault)
        => fault is QueueLifecycleCeilingFault.CancelStateVersion or QueueLifecycleCeilingFault.CancelDeliveryGeneration
            or QueueLifecycleCeilingFault.CancelLeaseVersion;

    private static MessageMetadata Metadata(MessageMetadata metadata, QueueLifecycleCeilingFault fault)
        => fault switch
        {
            QueueLifecycleCeilingFault.RedriveStateVersion or QueueLifecycleCeilingFault.CancelStateVersion
                or QueueLifecycleCeilingFault.ParkStateVersion => metadata with
                {
                    StateVersion = long.MaxValue
                },
            QueueLifecycleCeilingFault.RedriveDeliveryGeneration or QueueLifecycleCeilingFault.CancelDeliveryGeneration
                => metadata with
                {
                    DeliveryGeneration = long.MaxValue
                },
            QueueLifecycleCeilingFault.RedriveLeaseVersion or QueueLifecycleCeilingFault.CancelLeaseVersion
                => metadata with
                {
                    LeaseVersion = long.MaxValue
                },
            _ => metadata
        };

    private static QueueCounters Counters(QueueCounters counters, QueueLifecycleCeilingFault fault)
        => fault switch
        {
            QueueLifecycleCeilingFault.RedriveReadySequence => counters with { NextReadySequence = long.MaxValue },
            QueueLifecycleCeilingFault.RedriveOrderSequence => counters with { NextOrderSequence = long.MaxValue },
            QueueLifecycleCeilingFault.ParkSequence => counters with { NextParkedSequence = long.MaxValue },
            _ => counters
        };

    private static Mutation Mutation(QueueLaneRef lane, MessageMetadata metadata, QueueLifecycleCeilingFault fault)
    {
        if (IsPark(fault))
        {
            return new ParkPendingQueueMessage(lane.Queue, metadata.Id, metadata.StateVersion, metadata.DeliveryGeneration);
        }
        return IsCancel(fault)
            ? new CancelQueueMessage(lane.Queue, metadata.Id, metadata.StateVersion, metadata.DeliveryGeneration)
            : new RedriveQueueMessage(lane.Queue, metadata.Id, metadata.StateVersion, metadata.DeliveryGeneration);
    }

    private static void ChangeReferences(IKeyValueView view, QueueLaneRef lane, MessageMetadata metadata,
        ImmutableArray<KeyValueRecord>.Builder saved, List<KeyValueRecord> changes)
    {
        var key = metadata.State == MessageState.DeadLettered
            ? KeySpace.Partition(ParkedFamily, lane.Partition, lane.Queue, metadata.ParkedSequence, metadata.Id)
            : KeySpace.Partition(PendingFamily, lane.Partition, lane.Queue, metadata.Id);
        var bytes = Require(view, key);
        var replacement = metadata.State == MessageState.DeadLettered
            ? OrleansSerialization.Serialize(OrleansSerialization.Deserialize<QueueDeadLetterReference>(bytes) with
            {
                DeliveryGeneration = long.MaxValue
            })
            : OrleansSerialization.Serialize(OrleansSerialization.Deserialize<QueuePendingDeadLetterReference>(bytes) with
            {
                DeliveryGeneration = long.MaxValue
            });
        Save(view, key, replacement, saved, changes);
        if (metadata.ActiveOrderSequence > NoOrderPosition)
        {
            var body = view.GetRecord<MessageBody>(KeySpace.Partition(BodyFamily, lane.Partition, lane.Queue, metadata.Id))
                ?? throw new InvalidOperationException(MissingFixture);
            var order = KeySpace.Partition(OrderFamily, lane.Partition, lane.Queue, body.OrderingKey,
                metadata.ActiveOrderSequence, metadata.Id);
            Save(view, order, OrleansSerialization.Serialize(OrleansSerialization.Deserialize<QueueOrderReference>(Require(view, order)) with
            {
                DeliveryGeneration = long.MaxValue
            }), saved, changes);
        }
    }

    private static void Save(IKeyValueView view, byte[] key, byte[] changed,
        ImmutableArray<KeyValueRecord>.Builder saved, List<KeyValueRecord> changes)
    {
        saved.Add(new(key, Require(view, key)));
        changes.Add(new(key, changed));
    }

    private static byte[] Require(IKeyValueView view, byte[] key)
        => view.ReadOwnedValue(key) ?? throw new InvalidOperationException(MissingFixture);
}
