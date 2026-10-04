using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferStorage
{
    private const string CorruptCapacityMessage = "Persisted queue transfer capacity is inconsistent.";

    internal static byte[] IntentKey(QueueLaneRef source, Guid transferId)
        => KeySpace.Partition(RemoteTransferProtocol.IntentSpace, source.Partition, source.Queue,
            transferId.ToString(RemoteTransferProtocol.TransferIdFormat));

    internal static byte[] SourceCapacityKey(QueueLaneRef source)
        => KeySpace.Partition(RemoteTransferProtocol.SourceCapacitySpace, source.Partition, source.Queue);

    internal static byte[] TargetReceiptKey(QueueLaneRef source, Guid transferId, QueueLaneRef destination)
        => KeySpace.Partition(RemoteTransferProtocol.TargetReceiptSpace, destination.Partition, destination.Queue,
            source.Partition.TenantId, source.Partition.DatabaseId, source.Partition.TransactionDomainId,
            source.Partition.PartitionKey, source.Queue, transferId.ToString(RemoteTransferProtocol.TransferIdFormat));

    internal static byte[] TargetCapacityKey(QueueLaneRef destination)
        => KeySpace.Partition(RemoteTransferProtocol.TargetCapacitySpace, destination.Partition, destination.Queue);

    internal static RemoteTransferCapacity SourceCapacity(IKeyValueView view, QueueLaneRef source)
        => view.GetRecord<RemoteTransferCapacity>(SourceCapacityKey(source)) ?? new(0, 0);

    internal static RemoteTransferCapacity TargetCapacity(IKeyValueView view, QueueLaneRef destination)
        => view.GetRecord<RemoteTransferCapacity>(TargetCapacityKey(destination)) ?? new(0, 0);

    internal static RemoteTransferCapacity RequireSourceCounter(IKeyValueView view, QueueLaneRef source)
        => RequireCounter(view.GetRecord<RemoteTransferCapacity>(SourceCapacityKey(source)));

    internal static RemoteTransferCapacity RequireTargetCounter(IKeyValueView view, QueueLaneRef destination)
        => RequireCounter(view.GetRecord<RemoteTransferCapacity>(TargetCapacityKey(destination)));

    internal static long SourceAccountedBytes(RemoteTransferIntentRecord record)
    {
        var actualReceiptBytes = record.ReceiptToken is null ? 0 : Encoding.UTF8.GetByteCount(record.ReceiptToken);
        if (actualReceiptBytes > record.ReceiptReservationBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacityMessage);
        }
        return checked(SerializedBytes(record) + record.ReceiptReservationBytes - actualReceiptBytes);
    }

    internal static long SerializedBytes<T>(T record) => NativeSerialization.Serialize(record).LongLength;

    internal static RemoteTransferCapacity Add(RemoteTransferCapacity current, long bytes)
        => new(checked(current.StoredRecords + 1), checked(current.StoredBytes + bytes));

    internal static RemoteTransferCapacity Replace(RemoteTransferCapacity current, long oldBytes, long newBytes,
        DatabaseLimits limits)
    {
        RequireCounter(current);
        if (oldBytes > current.StoredBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacityMessage);
        }
        var bytes = checked(current.StoredBytes - oldBytes + newBytes);
        if (bytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The retained queue transfer capacity is exhausted.");
        }
        return new(current.StoredRecords, bytes);
    }

    internal static void RequireCapacity(RemoteTransferCapacity current, long additionalBytes, DatabaseLimits limits)
    {
        RequireCounter(current);
        if (current.StoredRecords >= limits.MaxScanRecords
            || additionalBytes < 0 || additionalBytes > limits.MaxBatchBytes - current.StoredBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The retained queue transfer capacity is exhausted.");
        }
    }

    private static RemoteTransferCapacity RequireCounter(RemoteTransferCapacity? capacity)
    {
        if (capacity is null || capacity.StoredRecords < 0 || capacity.StoredBytes < 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacityMessage);
        }
        return capacity;
    }
}
