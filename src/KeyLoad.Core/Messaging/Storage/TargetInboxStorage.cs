using System.Globalization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Messaging;

internal static class TargetInboxStorage
{
    internal static byte[] Key(CommitInboxRequest request)
        => KeySpace.Partition(TargetInboxProtocol.RecordSpace, request.Target.Partition, request.Target.Queue,
            request.HandlerScope, request.ExecutionGeneration.ToString(CultureInfo.InvariantCulture),
            request.Source.Partition.TenantId, request.Source.Partition.DatabaseId,
            request.Source.Partition.TransactionDomainId, request.Source.Partition.PartitionKey,
            request.Source.Queue, request.MessageId, request.DeliveryGeneration.ToString(CultureInfo.InvariantCulture));

    internal static byte[] CapacityKey(QueueLaneRef target)
        => KeySpace.Partition(TargetInboxProtocol.CapacitySpace, target.Partition, target.Queue);

    internal static string Identity(CommitInboxRequest request)
        => JsonData.Fingerprint(new
        {
            request.Target,
            request.Source,
            request.MessageId,
            request.DeliveryGeneration,
            request.HandlerScope,
            request.ExecutionGeneration
        });

    internal static TargetInboxCapacity Capacity(IKeyValueView view, QueueLaneRef target, bool required)
    {
        var current = view.GetRecord<TargetInboxCapacity>(CapacityKey(target));
        if (current is null && required)
        {
            throw Errors.Fail(ErrorCode.Corruption, TargetInboxProtocol.Corrupt);
        }
        current ??= new(TargetInboxProtocol.EmptyCount, TargetInboxProtocol.EmptyCount);
        if (current.Count < TargetInboxProtocol.EmptyCount || current.Bytes < TargetInboxProtocol.EmptyCount
            || (current.Count == TargetInboxProtocol.EmptyCount) != (current.Bytes == TargetInboxProtocol.EmptyCount))
        {
            throw Errors.Fail(ErrorCode.Corruption, TargetInboxProtocol.Corrupt);
        }
        RequireCapacityWitness(view, target, current);
        return current;
    }

    private static void RequireCapacityWitness(IKeyValueView view, QueueLaneRef target, TargetInboxCapacity capacity)
    {
        var firstBytes = TargetInboxProtocol.EmptyCount;
        var witness = view.VisitRange(KeySpace.Partition(TargetInboxProtocol.RecordSpace, target.Partition, target.Queue),
            1, (key, value) =>
            {
                firstBytes = checked((long)key.Length + value.Length);
                return true;
            });
        if ((witness.Records == TargetInboxProtocol.EmptyCount) != (capacity.Count == TargetInboxProtocol.EmptyCount)
            || capacity.Bytes < firstBytes
            || witness.HasMore && capacity.Count <= TargetInboxProtocol.MinimumCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, TargetInboxProtocol.Corrupt);
        }
    }

    internal static void RequireSelectedCapacity(TargetInboxCapacity capacity, long bytes)
    {
        if (capacity.Count < TargetInboxProtocol.MinimumCount || capacity.Bytes < bytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, TargetInboxProtocol.Corrupt);
        }
    }

    internal static TargetInboxCapacity Add(TargetInboxCapacity current, long bytes, InboxPolicy policy)
    {
        if (current.Count >= policy.MaxReceipts || bytes < TargetInboxProtocol.MinimumCount
            || bytes > policy.MaxBytes - current.Bytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TargetInboxProtocol.Exhausted);
        }
        return new(checked(current.Count + TargetInboxProtocol.MinimumCount), checked(current.Bytes + bytes));
    }
}
