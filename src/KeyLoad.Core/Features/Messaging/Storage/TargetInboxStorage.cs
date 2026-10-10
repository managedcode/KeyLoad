using System.Globalization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

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
        { throw Errors.Fail(ErrorCode.Corruption, TargetInboxProtocol.Corrupt); }
        current ??= new(TargetInboxProtocol.EmptyCount, TargetInboxProtocol.EmptyCount);
        if (current.Count < TargetInboxProtocol.EmptyCount || current.Bytes < TargetInboxProtocol.EmptyCount
            || (current.Count == TargetInboxProtocol.EmptyCount) != (current.Bytes == TargetInboxProtocol.EmptyCount))
        { throw Errors.Fail(ErrorCode.Corruption, TargetInboxProtocol.Corrupt); }
        return current;
    }

    internal static TargetInboxCapacity Add(TargetInboxCapacity current, long bytes, InboxPolicy policy)
    {
        if (current.Count >= policy.MaxReceipts || bytes < TargetInboxProtocol.MinimumCount
            || bytes > policy.MaxBytes - current.Bytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, TargetInboxProtocol.Exhausted); }
        return new(checked(current.Count + TargetInboxProtocol.MinimumCount), checked(current.Bytes + bytes));
    }
}
