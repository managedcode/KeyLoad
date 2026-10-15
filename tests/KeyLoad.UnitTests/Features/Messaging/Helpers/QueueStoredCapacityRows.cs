using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueStoredCapacityRows
{
    internal static QueueStoredCapacityRow[] Damage(ZoneTreeStore store, PartitionRef partition, QueueStoredCapacityFault fault)
    {
        var keys = fault == QueueStoredCapacityFault.UsageWithoutBodies
            ? new[] { Key(partition, QueueEnqueueColdProtocol.BodyFamily, QueueEnqueueColdProtocol.Ready),
                Key(partition, QueueEnqueueColdProtocol.BodyFamily, QueueEnqueueColdProtocol.Scheduled) }
            : new[] { Key(partition, QueueEnqueueColdProtocol.CountersFamily) };
        var rows = keys.Select(key => new QueueStoredCapacityRow(key, store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(QueueEnqueueAuthorityProtocol.MissingOwnedRow))).ToArray();
        store.Commit((transaction, _) =>
        {
            foreach (var row in rows)
            {
                if (fault == QueueStoredCapacityFault.UsageWithoutBodies)
                {
                    transaction.Delete(row.Key);
                }
                else
                {
                    transaction.PutRecord(row.Key, DamagedCounters(NativeSerialization.Deserialize<QueueCounters>(row.Original), fault));
                }
            }
            return true;
        });
        return rows;
    }

    internal static void Repair(ZoneTreeStore store, QueueStoredCapacityRow[] rows)
        => store.Commit((transaction, _) =>
        {
            foreach (var row in rows)
            {
                transaction.Put(row.Key, row.Original);
            }
            return true;
        });

    private static QueueCounters DamagedCounters(QueueCounters counters, QueueStoredCapacityFault fault)
        => fault switch
        {
            QueueStoredCapacityFault.ZeroUsageWithBodies => counters with { StoredMessages = QueueEnqueueAuthorityProtocol.Initial, StoredBytes = QueueEnqueueAuthorityProtocol.Initial },
            QueueStoredCapacityFault.RecordsWithoutBytes => counters with { StoredBytes = QueueEnqueueAuthorityProtocol.Initial },
            QueueStoredCapacityFault.BytesWithoutRecords => counters with { StoredMessages = QueueEnqueueAuthorityProtocol.Initial },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

    private static byte[] Key(PartitionRef partition, string family, params object?[] parts)
        => KeyLoad.Core.KeySpace.Partition(family, partition, [QueueEnqueueColdProtocol.Queue, .. parts]);
}
